using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.Services;
using Invoklet.Services.BuildCache;
using Invoklet.Services.Preferences;
using Invoklet.Services.Tabs;
using Invoklet.UserControls.Models;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Invoklet.UserControls;

public partial class EditorTabsControl : UserControl, IDisposable, ITabDiskHost, IActiveTabSource, ITabReverter
{
    private readonly Dictionary<EditorTabItemViewModel, (TabItem Tab, EditorsTabItemControl Control)> _tabs = new();
    private readonly TabLifecycleManager? _lifecycle;
    private readonly TabDiskSync? _diskSync;
    private readonly CancellationTokenSource _closed = new();
    // Tabs whose editor services are still being created: closing one cancels its creation.
    private readonly Dictionary<EditorTabItemViewModel, CancellationTokenSource> _creating = new();
    private IDisposable? _activeTabSub;
    private bool _disposed;
    private readonly ILogger<EditorsTabItemControl>? _runLogger;
    private readonly ConnectionDriverRegistry? _connectionDrivers;
    private readonly ConnectionSchemas? _connectionSchemas;
    private readonly EditorTimings? _timings;

    private MainWindowViewModel? _vm;

    // ── Set in DI constructor ─────────────────────────────────────────────────

    private readonly IThemeService? _themeService;
    private readonly IWindowService? _windowService;
    private readonly ILanguageHostRegistry? _registry;
    private readonly IPreferencesService? _prefs;
    private readonly IBuildCacheService? _buildCache;

    // ── Empty ctor: used by XAML previewer only ───────────────────────────────

    public EditorTabsControl()
    {
        InitializeComponent();
    }

    // ── DI ctor: used at runtime ──────────────────────────────────────────────

    public EditorTabsControl(
        IThemeService themeService,
        IWindowService windowService,
        ILanguageHostRegistry registry,
        IPreferencesService prefs,
        IBuildCacheService buildCache, ILogger<EditorsTabItemControl>? runLogger = null,
        ConnectionDriverRegistry? connectionDrivers = null, ConnectionSchemas? connectionSchemas = null,
        TabLifecycleManager? lifecycle = null, EditorTimings? timings = null, IServiceProvider? services = null)
    {
        InitializeComponent();
        _services = services;
        _lifecycle = lifecycle;
        _timings = timings;
        _diskSync = new TabDiskSync(windowService);
        _connectionDrivers = connectionDrivers;
        _connectionSchemas = connectionSchemas;
        _themeService = themeService;
        _windowService = windowService;
        _registry = registry;
        _prefs = prefs;
        _buildCache = buildCache;
        _runLogger = runLogger;
        _themeService.ThemeChanged += OnThemeChanged;
        WireStrip();
    }

    // The strip's buttons are parts of the TabControl's template ("+" after the last tab, the list of tabs at the right), and dragging a
    // tab along the strip moves it.
    private void WireStrip()
    {
        var fileTabControl = this.FindControl<TabControl>("FileTabControl")!;
        fileTabControl.TemplateApplied += (_, e) =>
        {
            _stripRow = e.NameScope.Find<Grid>("PART_StripRow");
            ShowStripRow();
            if (e.NameScope.Find<Button>("PART_AddTab") is { } add) add.Click += AddFile_Click;
            if (e.NameScope.Find<Button>("PART_AllTabs") is { } all) all.Click += ShowAllTabs_Click;
            if (e.NameScope.Find<ScrollViewer>("PART_TabStripScrollViewer") is { } strip)
            {
                _stripScroll = strip;
                strip.AddHandler(PointerWheelChangedEvent, OnStripWheel, RoutingStrategies.Tunnel);
            }
        };
        AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag());
    }

    // ── Called by MainWindow after construction ───────────────────────────────

    public void Attach(MainWindowViewModel vm)
    {
        if (_vm is not null)
            throw new InvalidOperationException("EditorTabsControl can only be attached once.");

        _vm = vm;
        Strips.Add(this);

        var fileTabControl = this.FindControl<TabControl>("FileTabControl")!;
        fileTabControl.SelectionChanged += OnFileTabSelectionChanged;

        if (_windowId == 0) _lifecycle?.Attach(() => Shown, OnContentNotRestored);
        else _windowWatch = _lifecycle?.WatchWindow(() => Shown);
        vm.Tabs.CollectionChanged += OnTabsCollectionChanged;
        _activeTabSub = vm.WhenAnyValue(x => x.ActiveTab).Subscribe(OnActiveTabChanged);
        vm.DependenciesWindowClosed += OnDependenciesWindowClosed;
        vm.ScriptsFolderChanged += OnScriptsFolderChanged;

        if (_windowId != 0)
        {
            // The session and its restore are the main window's.
            _sessionRestored = true;
            _workspaceShown.TrySetResult();
            return;
        }

        // Restores the previous session's open tabs
        // (in order) + active tab instead of always starting from one blank tab.
        _ = RestoreSessionAsync(vm);
    }

    // Done once the restored session's tabs all have their headers and the active one shows its
    // script: tabs appear one by one (each first creates its language services, see
    // OnTabsCollectionChanged), so the window stays hidden behind the splash until then.
    internal Task WorkspaceShown => _workspaceShown.Task;
    private readonly TaskCompletionSource _workspaceShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sessionRestored;

    private async Task RestoreSessionAsync(MainWindowViewModel vm)
    {
        try { await vm.RestoreSessionAsync(); }
        finally
        {
            _sessionRestored = true;
            SignalWorkspaceShownIfDone();
        }
    }

    private void SignalWorkspaceShownIfDone()
    {
        if (_disposed || _sessionRestored && _vm is not null && _vm.Tabs.Where(t => t.WindowId == _windowId).All(_tabs.ContainsKey))
            _workspaceShown.TrySetResult();
    }

    // Dependencies window copies its draft into the active tab's MdRunFrontMatter only on OK.
    // On close: if that changed anything, push the new packages/imports into that tab's own
    // IntelliSense instance and flush to disk immediately so the change survives an app
    // close/crash instead of waiting for the next Run or tab close.
    private void OnScriptsFolderChanged() => _ = CheckFilesChangedOnDiskAsync();

    private void OnDependenciesWindowClosed()
    {
        GetActiveControl()?.OnDependenciesEdited();
    }

    // ── Tab lifecycle ──────────────────────────────────────────────────────────

    // The changes of the tab collection that are being handled (a tab is added once its language services exist): complete when every tab that
    // was added has its control. For a test to wait for instead of polling; nothing else uses it.
    private readonly HashSet<Task> _tabChanges = [];

    internal Task TabsReadyForTests() => Task.WhenAll(_tabChanges.ToArray());

    private async void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Runs up to its first wait here, so the change is tracked before this returns; an error is raised here as when this did the work itself.
        var work = TabsChangedAsync(e);
        _tabChanges.Add(work);
        try { await work; }
        finally { _tabChanges.Remove(work); }
    }

    private async Task TabsChangedAsync(NotifyCollectionChangedEventArgs e)
    {
        var fileTabControl = this.FindControl<TabControl>("FileTabControl")!;

        // A tab moved along the strip keeps its control: only its header changes place.
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            foreach (EditorTabItemViewModel moved in e.NewItems ?? Array.Empty<EditorTabItemViewModel>())
                MoveTabItem(fileTabControl, moved);
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (EditorTabItemViewModel vm in e.NewItems)
            {
                if (vm.WindowId != _windowId) continue;
                // The expensive part (the language's editor services — see CreateIntelliSenseAsync)
                // runs off the UI thread so opening a script doesn't freeze the app. By the time we
                // get back here, the tab may already have been closed again (or the whole control
                // torn down) — don't resurrect it in that case.
                using var creation = new CancellationTokenSource();
                _creating[vm] = creation;
                IIntelliSenseService? intelliSense;
                try
                {
                    intelliSense = await CreateIntelliSenseAsync(vm, creation.Token);
                }
                finally
                {
                    _creating.Remove(vm);
                }
                if (_disposed || !(_vm?.Tabs.Contains(vm) ?? false))
                {
                    // The tab never got the service, so it is still ours to dispose. Nobody else owns
                    // vm's disposal in this case either: MainWindowViewModel.CloseTab doesn't dispose
                    // it itself, it relies on the (normally synchronous) OldItems branch below —
                    // which already ran and only cancelled the creation, since _tabs had no entry yet.
                    try
                    {
                        intelliSense?.Dispose();
                    }
                    finally
                    {
                        vm.Dispose();
                        SignalWorkspaceShownIfDone();
                    }
                    continue;
                }

                // From here on the tab owns the service and disposes it with itself; until the control has taken it, a failure
                // to build the control must not leave the service behind.
                EditorsTabItemControl ctrl;
                try
                {
                    ctrl = CreateControl(vm, intelliSense);
                }
                catch
                {
                    intelliSense?.Dispose();
                    throw;
                }

                // Where vm is among the tabs already shown: the end, unless it took the place of one.
                var position = _vm!.Tabs.TakeWhile(t => !ReferenceEquals(t, vm)).Count(_tabs.ContainsKey);
                var tab = AddTabItem(ctrl, vm, fileTabControl, position);
                WatchDrags(tab);
                vm.MoveToNewWindow = () => MoveToNewWindow(vm);
                _tabs[vm] = (tab, ctrl);
                if (_lifecycle is not null)
                {
                    ctrl.SpillStore = _lifecycle.Spill;
                    _lifecycle.Register(vm, ctrl);
                }

                // ActiveTab may already have been set to vm (e.g. it's the tab that was just
                // opened) before this entry existed — OnActiveTabChanged no-ops in that case, so
                // select it explicitly now if it's still the active one.
                if (ReferenceEquals(_vm?.ActiveTab, vm) || ReferenceEquals(_shown, vm))
                    fileTabControl.SelectedItem = tab;
                _lifecycle?.Rebalance();
                SignalWorkspaceShownIfDone();
            }
        }

        if (e.OldItems is not null)
        {
            foreach (EditorTabItemViewModel vm in e.OldItems)
            {
                if (_creating.TryGetValue(vm, out var creation)) creation.Cancel();
                if (!_tabs.TryGetValue(vm, out var entry)) continue;
                // No save here: a write failure discovered only now would have nowhere
                // to go — the tab and its editor are being destroyed. Every removal path resolves
                // unsaved content BEFORE removing (MainWindowViewModel.
                // ResolveUnsavedChangesBeforeCloseAsync / TrySaveTabsBeforeFileOperation).
                _lifecycle?.Unregister(vm);
                entry.Control.Dispose();
                fileTabControl.Items.Remove(entry.Tab);
                _tabs.Remove(vm);
                vm.Dispose();
            }
            SignalWorkspaceShownIfDone();
            RaiseEmptiedIfNoTabs();
        }
    }

    // ── Selection sync ─────────────────────────────────────────────────────────

    private void OnFileTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tc || _vm is null || _movingTab) return;
        var selected = _tabs.FirstOrDefault(kv => kv.Value.Tab == tc.SelectedItem as TabItem).Key;

        // tc.SelectedItem transiently goes null while Items is being mutated — e.g. OpenTab closing
        // the initial blank tab right after opening a real file removes the blank tab's TabItem
        // before the new tab's own TabItem exists yet (CreateIntelliSenseAsync is still loading it off
        // the UI thread), leaving the TabControl briefly empty. That's not a real "no tab selected"
        // user action; nulling ActiveTab here would strand it permanently, since the code above that
        // would normally re-select the new tab once it finishes loading only does so
        // `if (ReferenceEquals(_vm?.ActiveTab, vm))`.
        if (selected is not null) _shown = selected;
        if (selected is null && (_vm.Tabs.Any(t => t.WindowId == _windowId) || _windowId != 0)) return;
        // A header taken out of a window not in use (its tab moved to another window) selects another one there, not in the workspace.
        if (AnotherWindowInUse()) return;

        _vm.ActiveTab = selected;
    }

    private void OnActiveTabChanged(EditorTabItemViewModel? vm)
    {
        if (vm is null || vm.WindowId != _windowId) return;
        _shown = vm;

        var fileTabControl = this.FindControl<TabControl>("FileTabControl")!;
        // A tab of this window chosen elsewhere (the list of tabs, opening its file again from another window): the window comes forward.
        if (Strips.Count > 1 && TopLevel.GetTopLevel(this) is Window { IsVisible: true, IsActive: false } window) window.Activate();
        if (_tabs.TryGetValue(vm, out var entry))
        {
            if (fileTabControl.SelectedItem != entry.Tab) fileTabControl.SelectedItem = entry.Tab;
            // A tab chosen from the list, or opened at the end, may lie past the strip's visible part.
            Dispatcher.Post(() => entry.Tab.BringIntoView(), DispatcherPriority.Loaded);
        }

        _lifecycle?.Activated(vm);
    }

    // Only the tab shown colours its text.
    private void SyncHighlighting()
    {
        foreach (var (tabVm, (_, ctrl)) in _tabs)
            ctrl?.SetHighlightingActive(ReferenceEquals(tabVm, Shown));
    }

    internal Task IdleForTests() => _lifecycle?.WhenIdleAsync() ?? Task.CompletedTask;

    /// <summary>Some tab has output of a run that is not on disk yet.</summary>
    internal bool HasPendingOutputWrites() => _tabs.Values.Any(t => !t.Control.OutputWrites.IsCompleted);

    /// <summary>
    /// Completes once the output of the runs of every tab is on disk (so that the last result is not lost when the application is closed), or when
    /// <paramref name="limit"/> has passed: False then, and what was not written is not waited for any more. A write that failed has been logged by
    /// its writer; there is nothing more to wait for.
    /// </summary>
    internal async Task<bool> FlushOutputWritesAsync(TimeSpan limit)
    {
        var pending = _tabs.Values.Select(t => t.Control.OutputWrites).Where(task => !task.IsCompleted).ToList();
        if (pending.Count == 0) return true;
        try
        {
            await Task.WhenAll(pending).WaitAsync(limit);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception)
        {
            // A failed write: reported where it happened.
        }
        return true;
    }

    internal bool IsLoadedForTests(EditorTabItemViewModel vm) => _lifecycle?.IsContentLoaded(vm) ?? HasControl(vm);

    // A tab's text could not be brought back from disk: it stays empty and read-only, and saving it fails,
    // so nothing is overwritten.
    private void OnContentNotRestored(EditorTabItemViewModel tab)
        => _ = _windowService?.ShowErrorAsync("Tab Not Loaded", $"Could not read back the text of '{tab.FileName}'.");

    // ── Theme ──────────────────────────────────────────────────────────────────

    // Test-only (InternalsVisibleTo): the control showing a tab.
    internal EditorsTabItemControl? ControlForTests(EditorTabItemViewModel vm) => _tabs.TryGetValue(vm, out var entry) ? entry.Control : null;

    private void OnThemeChanged(ActiveTheme theme)
    {
        foreach (var (_, ctrl) in _tabs.Values)
            ctrl.ApplyTheme(theme);
    }

    // ── Files changed by other programs ───────────────────────────────────────

    /// <summary>
    /// Brings open tabs up to date with files another program changed (see <see cref="TabDiskSync"/>).
    /// Runs when Invoklet's window is activated and when the scripts folder reports a change.
    /// </summary>
    public Task CheckFilesChangedOnDiskAsync() => _diskSync?.CheckAsync(this, _closed.Token) ?? Task.CompletedTask;

    // The per-tab confirmation above only fires when a
    // SPECIFIC tab is closed — quitting the whole app skipped it entirely. MainWindow's Closing
    // handler checks this first (cheap, synchronous) before deciding whether it even needs to cancel
    // the close and await ConfirmCloseAllUnsavedTabsAsync below. Dirty named tabs count
    // too — they used to be written by Dispose after the window was already gone, so a failed
    // write lost the edits silently.
    public bool HasUnsavedTabs()
        => _vm?.Tabs.Any(t => t.IsDirty) ?? false;

    // Walks every dirty tab (not just the active one) through the same decision a single tab's close
    // uses (MainWindowViewModel.ResolveUnsavedChangesBeforeCloseAsync: named tabs are written in
    // place; untitled tabs and failed writes ask Save/Don't Save/Cancel) — stops at the first one
    // that can't be resolved rather than working through the rest, same as VS Code's behavior on
    // quit. Deliberately doesn't remove tabs from Tabs or touch _tabs itself — if the quit is
    // cancelled, every tab must still be there exactly as it was.
    public async Task<bool> ConfirmCloseAllUnsavedTabsAsync()
    {
        if (_vm is null) return true;

        // Snapshotted: nothing in this loop removes a tab, but SaveTabAsAsync does rebind FilePath —
        // iterating a live view of Tabs while that happens is asking for trouble even if today's
        // mutation pattern happens not to invalidate the enumerator.
        foreach (var vm in _vm.Tabs.ToList())
        {
            if (!vm.IsDirty || !_tabs.ContainsKey(vm)) continue;
            if (!await _vm.ResolveUnsavedChangesBeforeCloseAsync(vm)) return false;
        }

        return true;
    }

    // A tab whose text is on disk is read from its file when it is selected again, so there is nothing to reload.
    private bool HasControl(EditorTabItemViewModel vm) => _tabs.TryGetValue(vm, out var entry) && !entry.Control.IsContentReleased;

    private EditorsTabItemControl? GetActiveControl()
    {
        if (_vm?.ActiveTab is { } vm && _tabs.TryGetValue(vm, out var entry))
            return entry.Control;
        return null;
    }

    // ── "Show all open tabs" dropdown ───────────────────────────────────────────

    private void ShowAllTabs_Click(object? sender, RoutedEventArgs e)
    {
        if (BuildAllTabsMenu() is not { } menu || sender is not Control anchor) return;
        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    // internal (not private): lets tests inspect the built menu's items/headers/click-wiring
    // directly, rather than only through the ContextMenu popup interaction itself, which headless
    // Avalonia has no reliable way to drive. Built fresh on every click
    // (not a persistent binding — this control doesn't set its own DataContext to the ViewModel, it
    // talks to it through _vm directly everywhere else too) rather than keeping one around that
    // would go stale the moment a tab opens/closes.
    internal ContextMenu? BuildAllTabsMenu()
    {
        if (_vm is null) return null;

        var menu = new ContextMenu();
        foreach (var tabVm in _vm.Tabs)
        {
            // Fully qualified with global:: on top — Control (an EditorTabsControl base class) has
            // its own inherited FontWeight instance property, shadowing the enum type name throughout
            // this whole class ("cannot be accessed with an instance reference" without the
            // qualification); and Invoklet.Avalonia is itself a real sibling namespace here
            // (Invoklet.Avalonia.Themes etc.), so plain "Avalonia.Media" would resolve relative to
            // Invoklet first and fail to find it, rather than falling back to the global Avalonia.
            var weight = ReferenceEquals(tabVm, _vm.ActiveTab)
                ? global::Avalonia.Media.FontWeight.Bold
                : global::Avalonia.Media.FontWeight.Normal;
            var header = new TextBlock { Text = tabVm.FileName, FontWeight = weight };
            if (tabVm.IsDirty && _themeService?.GetBrush("Invoklet.Tab.DirtyBrush") is { } dirtyBrush)
                header.Foreground = dirtyBrush;

            var item = new MenuItem { Header = header };
            item.Click += (_, _) => _vm.ActiveTab = tabVm;
            menu.Items.Add(item);
        }

        return menu;
    }

    // ── "+" button ─────────────────────────────────────────────────────────────

    private async void AddFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var kind = await new Windows.NewFileKindDialog().ShowDialog<MdRunKind?>(owner);
        if (kind is null) return; // cancelled

        _vm.NewMdRunFile(kind: kind.Value);
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspaceShown.TrySetResult();
        if (_themeService is not null) _themeService.ThemeChanged -= OnThemeChanged;
        if (_vm is not null)
        {
            _vm.Tabs.CollectionChanged -= OnTabsCollectionChanged;
            _vm.DependenciesWindowClosed -= OnDependenciesWindowClosed;
            _vm.ScriptsFolderChanged -= OnScriptsFolderChanged;
        }

        _activeTabSub?.Dispose();
        _closed.Cancel();
        if (_vm is not null) Strips.Remove(this);
        if (_windowId == 0) _lifecycle?.Detach();
        _windowWatch?.Dispose();
        foreach (var creation in _creating.Values.ToArray()) creation.Cancel();

        // No save here: unsaved content was already resolved by MainWindow's Closing
        // confirmation — saving again would overwrite a "Don't Save" choice and report a failure
        // into a window that no longer exists.
        foreach (var (tabVm, (_, ctrl)) in _tabs)
        {
            ctrl.Dispose();
            tabVm.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Each tab gets its own editor services from its language's module: they are stateful (one
    // workspace and document per instance — each script can reference different NuGet packages), so
    // a shared instance would let one tab's references and last-synced text stomp another tab's.
    // Creating one can be real disk/CPU work with no Avalonia dependency (C# loads metadata for every
    // BCL assembly), so it runs off the UI thread instead of freezing the app on open.
    //
    // The language comes from the file's own front matter (the same cheap parse EditorsTabItemControl.
    // SetDocumentContent does again later) or, for a brand-new, never-saved tab (vm.FilePath is null,
    // nothing to peek), from the tab's own pending choice — a new VB tab must not get the services of
    // the default language. An invalid document (PeekDocumentLanguage → null) opens as raw text, and a
    // language without editor services gets none: both without any language-specific test here.
    // A creation cancelled because the tab closed comes back as null.
    private async Task<IIntelliSenseService?> CreateIntelliSenseAsync(EditorTabItemViewModel vm, CancellationToken cancellationToken)
    {
        var language = vm.RestoredRecovery is { } recovered
            ? recovered.Document?.FrontMatter.Language
            : vm.FilePath is null ? vm.NewFileLanguage : MdRunFilePeek.PeekDocumentLanguage(vm.FilePath);
        if (_registry is not { } registry || language is not { } known || known == MdRunLanguage.Unknown) return null;
        var id = known.ToLanguageId();

        var work = Task.Run(() => registry.CreateIntelliSense(id, cancellationToken), cancellationToken);
        // The tab must not wait for a creation that never ends or ignores the cancellation (a language's start-up that hangs would
        // keep the tab, and the splash screen behind it, for ever): after the timeout, or once the tab is closed, it goes on without
        // services, and whatever the creation returns later is disposed by whoever it reaches.
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finished = await Task.WhenAny(work, Task.Delay(ServiceCreationTimeout, timer.Token));
        await timer.CancelAsync(); // the timer of a creation that finished in time must not run on
        if (!ReferenceEquals(finished, work))
        {
            _ = work.ContinueWith(late => DisposeLate(late, id), TaskScheduler.Default);
            if (!cancellationToken.IsCancellationRequested)
                _runLogger?.LogWarning("The editor services of {Language} were not ready after {Timeout}; the tab opens without them.", id, ServiceCreationTimeout);
            return null;
        }

        try
        {
            return await work;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            // A language whose services cannot start must not take the application down from an async void handler: the tab opens
            // as plain text.
            _runLogger?.LogWarning(ex, "The editor services of {Language} could not be created; the tab opens without them.", id);
            return null;
        }
    }

    // What a creation returns after nobody is waiting for it any more. A failure to dispose it is logged: nothing else can see it.
    private void DisposeLate(Task<IIntelliSenseService?> late, string language)
    {
        if (!late.IsCompletedSuccessfully) return;
        try
        {
            late.Result?.Dispose();
        }
        catch (Exception ex)
        {
            _runLogger?.LogWarning(ex, "The editor services of {Language} that arrived late could not be disposed.", language);
        }
    }

    /// <summary>How long a tab waits for the editor services of its language before it opens without them.</summary>
    internal TimeSpan ServiceCreationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // Avalonia controls (CodeTextEditor, InitializeComponent, ...) must be constructed on the
    // UI thread, so the control itself is created here, synchronously.
    private EditorsTabItemControl CreateControl(EditorTabItemViewModel vm, IIntelliSenseService? intelliSense)
    {
        return new EditorsTabItemControl(vm,
            _themeService!,
            _windowService!,
            _registry!,
            _prefs!,
            intelliSense,
            _buildCache!, _runLogger, _connectionDrivers, _connectionSchemas, _timings
        );
    }

    private void MoveTabItem(TabControl tabControl, EditorTabItemViewModel vm)
    {
        if (_vm is null || !_tabs.TryGetValue(vm, out var entry)) return;
        var position = _vm.Tabs.TakeWhile(t => !ReferenceEquals(t, vm)).Count(_tabs.ContainsKey);
        if (tabControl.Items.IndexOf(entry.Tab) == position) return;
        var selected = tabControl.SelectedItem;
        // Taking the selected header out would select another tab for a moment, and with it another script.
        _movingTab = true;
        try
        {
            tabControl.Items.Remove(entry.Tab);
            tabControl.Items.Insert(position, entry.Tab);
            tabControl.SelectedItem = selected;
        }
        finally
        {
            _movingTab = false;
        }
    }

    private bool _movingTab;

    // The wheel scrolls the strip sideways (it has no scroll bar of its own): either direction of a wheel or a trackpad.
    private static void OnStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer strip) return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        strip.Offset = strip.Offset.WithX(Math.Clamp(strip.Offset.X - delta * 40, 0, Math.Max(0, strip.Extent.Width - strip.Viewport.Width)));
        e.Handled = true;
    }

    // ── Dragging a tab along the strip ─────────────────────────────────────────

    // A press on a header becomes a drag once the pointer has gone this far; less is a click that selects the tab.
    private const double DragThreshold = 4;

    // How near an end of the strip the pointer scrolls it, and by how much a move.
    private const double ScrollZone = 32, ScrollStep = 12;

    // How far below the strip a tab dropped goes to a window of its own (less is still a move along the strip).
    private const double TearOffDistance = 60;

    // A drag in progress: the header pressed and where on it, where the press was in the strip's coordinates, and, once it moves, the
    // header's picture following the pointer and the mark where the tab will go. Both lie in the window's overlay layer, so the tab can be
    // carried anywhere over the window.
    private sealed class TabDrag(TabItem tab, Point grab, Point start)
    {
        public TabItem Tab { get; } = tab;
        public Point Grab { get; } = grab;
        public Point Start { get; } = start;
        public Control? Ghost { get; set; }
        public Control? Mark { get; set; }
        public int Insert { get; set; } = -1;
    }

    private TabDrag? _drag;

    // The strip's scrolled part, from the TabControl's template.
    private ScrollViewer? _stripScroll;

    private void WatchDrags(TabItem tab) => tab.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TabItem tab || !e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
        _drag = new TabDrag(tab, e.GetPosition(tab), e.GetPosition(this));
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag || _vm is null) return;
        var at = e.GetPosition(this);
        if (drag.Ghost is null)
        {
            if (Math.Abs(at.X - drag.Start.X) < DragThreshold && Math.Abs(at.Y - drag.Start.Y) < DragThreshold) return;
            if (!StartDrag(drag)) return;
            e.Pointer.Capture(this);
        }
        if (global::Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is not { } overlay) return;
        var inOverlay = e.GetPosition(overlay);
        Canvas.SetLeft(drag.Ghost!, inOverlay.X - drag.Grab.X);
        Canvas.SetTop(drag.Ghost!, inOverlay.Y - drag.Grab.Y);
        ScrollNearEnds(e);
        ShowMark(drag, overlay, e);
        e.Handled = true;
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } drag) return;
        if (drag.Ghost is not null)
        {
            e.Handled = true;
            var tearOff = IsTearOff(e);
            var screen = TopLevel.GetTopLevel(this) is { } top ? top.PointToScreen(e.GetPosition(top) - drag.Grab) : default;
            if (_vm is not null && TabOf(drag.Tab) is { } moving)
            {
                EndDrag();
                e.Pointer.Capture(null);
                if (tearOff) MoveToNewWindow(moving, screen);
                else if (drag.Insert >= 0) _vm.MoveTab(moving, FinalIndex(moving, drag.Insert));
                return;
            }
        }
        EndDrag();
        e.Pointer.Capture(null);
    }

    // The header's picture under the pointer, the header itself faded where it was, and the pointer showing a move.
    private bool StartDrag(TabDrag drag)
    {
        if (global::Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is not { } overlay || TabOf(drag.Tab) is not { } vm) return false;
        drag.Ghost = new Border
        {
            Width = drag.Tab.Bounds.Width,
            Height = drag.Tab.Bounds.Height,
            Opacity = 0.85,
            IsHitTestVisible = false,
            Background = ThemeBrush("Invoklet.Editor.BackgroundBrush"),
            BorderBrush = ThemeBrush("Invoklet.Tab.AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = this.TryFindResource("Invoklet.Radius.Control", ActualThemeVariant, out var radius) && radius is CornerRadius r ? r : default,
            BoxShadow = new global::Avalonia.Media.BoxShadows(new global::Avalonia.Media.BoxShadow { OffsetY = 4, Blur = 12, Color = global::Avalonia.Media.Color.FromArgb(0x60, 0, 0, 0) }),
            Child = new TextBlock
            {
                Text = vm.FileName,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Foreground = ThemeBrush("Invoklet.Text.PrimaryBrush"),
            },
        };
        drag.Mark = new Border
        {
            Width = 2,
            Height = drag.Tab.Bounds.Height,
            IsHitTestVisible = false,
            IsVisible = false,
            Background = ThemeBrush("Invoklet.Tab.AccentBrush") ?? global::Avalonia.Media.Brushes.DodgerBlue,
        };
        overlay.Children.Add(drag.Mark);
        overlay.Children.Add(drag.Ghost);
        drag.Tab.Opacity = 0.35;
        Cursor = new Cursor(StandardCursorType.DragMove);
        return true;
    }

    private void EndDrag()
    {
        if (_drag is not { } drag) return;
        _drag = null;
        if (global::Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is { } overlay)
        {
            if (drag.Ghost is not null) overlay.Children.Remove(drag.Ghost);
            if (drag.Mark is not null) overlay.Children.Remove(drag.Mark);
        }
        drag.Tab.Opacity = 1;
        Cursor = null;
    }

    // The mark goes between the two headers the pointer is between, whatever its height: the tab is dropped where it shows.
    private void ShowMark(TabDrag drag, Visual overlay, PointerEventArgs e)
    {
        var others = Headers().Where(t => !ReferenceEquals(t, drag.Tab)).ToList();
        if (drag.Mark is null) return;
        // Far below the strip, or out of the window, the tab goes to a window of its own: no place along the strip is shown.
        if (others.Count == 0 || IsTearOff(e))
        {
            drag.Mark.IsVisible = false;
            drag.Insert = -1;
            return;
        }
        var x = e.GetPosition(others[0].Parent as Visual).X;
        drag.Insert = others.Count(t => t.Bounds.Left + t.Bounds.Width / 2 < x);
        var edge = drag.Insert < others.Count ? others[drag.Insert] : others[^1];
        var origin = edge.TranslatePoint(new Point(drag.Insert < others.Count ? 0 : edge.Bounds.Width, 0), overlay);
        if (origin is not { } at) return;
        Canvas.SetLeft(drag.Mark, at.X - 1);
        Canvas.SetTop(drag.Mark, at.Y);
        drag.Mark.IsVisible = true;
    }

    private bool IsTearOff(PointerEventArgs e)
    {
        if (_stripScroll is not { } strip || TopLevel.GetTopLevel(this) is not { } top) return false;
        var inWindow = e.GetPosition(top);
        var outOfWindow = inWindow.X < 0 || inWindow.Y < 0 || inWindow.X > top.Bounds.Width || inWindow.Y > top.Bounds.Height;
        return outOfWindow || e.GetPosition(strip).Y > strip.Bounds.Height + TearOffDistance;
    }

    // Near an end of the strip it scrolls, so a tab can be carried to one out of sight.
    private void ScrollNearEnds(PointerEventArgs e)
    {
        if (_stripScroll is not { } strip) return;
        var x = e.GetPosition(strip).X;
        var step = x < ScrollZone ? -ScrollStep : x > strip.Viewport.Width - ScrollZone ? ScrollStep : 0;
        if (step != 0)
            strip.Offset = strip.Offset.WithX(Math.Clamp(strip.Offset.X + step, 0, Math.Max(0, strip.Extent.Width - strip.Viewport.Width)));
    }

    // Where the tab goes among all the tabs, from its place among the headers it was dropped between.
    private int FinalIndex(EditorTabItemViewModel moving, int insert)
    {
        var others = Headers().Where(t => TabOf(t) is { } vm && !ReferenceEquals(vm, moving)).ToList();
        if (insert >= others.Count) return _vm!.Tabs.Count - 1;
        var target = _vm!.Tabs.IndexOf(TabOf(others[insert])!);
        return target > _vm.Tabs.IndexOf(moving) ? target - 1 : target;
    }

    private List<TabItem> Headers() => [.. this.FindControl<TabControl>("FileTabControl")!.Items.OfType<TabItem>()];

    private global::Avalonia.Media.IBrush? ThemeBrush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as global::Avalonia.Media.IBrush : null;

    private EditorTabItemViewModel? TabOf(TabItem tab) => _tabs.FirstOrDefault(kv => ReferenceEquals(kv.Value.Tab, tab)).Key;

    private static TabItem AddTabItem(EditorsTabItemControl ctrl, EditorTabItemViewModel vm, TabControl tabControl, int position)
    {
        ctrl.Resources.TryGetResource("TabHeaderTemplate", null, out var tmpl);
        var tab = new TabItem
        {
            Header = vm,
            HeaderTemplate = (IDataTemplate)tmpl!,
            Content = ctrl,
        };
        tabControl.Items.Insert(position, tab);
        return tab;
    }
}
