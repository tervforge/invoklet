using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Invoklet.UserControls.Models;
using Invoklet.Windows;
using Invoklet.Windows.Models;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Invoklet.UserControls;

// A tab can be moved out of the main window's strip into a window of its own, which shows only that tab's body (no row of headers) and
// gives the tab back to the main window when it closes. The tabs, the session and the recovery stay the workspace's (MainWindowViewModel.Tabs);
// each window's strip shows the tabs of its window (EditorTabItemViewModel.WindowId) and hands a tab, with its editor, to another strip. The
// workspace's ActiveTab is the tab of the window being used, so the menus and the dialogs act on it.
public partial class EditorTabsControl
{
    // The strips of one workspace: a tab moves between them, and closing the main window gathers their tabs.
    private static readonly ConditionalWeakTable<MainWindowViewModel, List<EditorTabsControl>> s_strips = new();
    private static int s_lastWindowId;

    private readonly IServiceProvider? _services;
    private int _windowId;
    private EditorTabItemViewModel? _shown;
    private IDisposable? _windowWatch;
    private Grid? _stripRow;

    /// <summary>0 for the main window's strip.</summary>
    internal int WindowId => _windowId;

    /// <summary>The last of a window's own tabs went away (closed, or moved to another window): the window closes.</summary>
    internal event Action? Emptied;

    private List<EditorTabsControl> Strips => _vm is null ? [this] : s_strips.GetOrCreateValue(_vm);

    /// <summary>The tab this strip shows.</summary>
    internal EditorTabItemViewModel? Shown =>
        _shown is { } shown && shown.WindowId == _windowId && _tabs.ContainsKey(shown) ? shown : SelectedTab();

    internal IReadOnlyList<EditorTabItemViewModel> OwnTabs => [.. _tabs.Keys];

    private EditorTabItemViewModel? SelectedTab()
    {
        var selected = this.FindControl<TabControl>("FileTabControl")!.SelectedItem;
        return _tabs.FirstOrDefault(kv => ReferenceEquals(kv.Value.Tab, selected)).Key;
    }

    // Another window of the workspace is the one in use: what this strip selects is not the workspace's active tab.
    private bool AnotherWindowInUse() => Strips.Any(s => !ReferenceEquals(s, this) && TopLevel.GetTopLevel(s) is Window { IsActive: true });

    /// <summary>The window became the one in use: its tab is the workspace's active one.</summary>
    internal void WindowActivated()
    {
        if (_vm is not null && Shown is { } shown && !ReferenceEquals(_vm.ActiveTab, shown)) _vm.ActiveTab = shown;
    }

    /// <summary>Opens a window of its own for <paramref name="tab"/>, a tab of the main window, at <paramref name="at"/> on the screen when
    /// given.</summary>
    internal EditorTabsControl? MoveToNewWindow(EditorTabItemViewModel tab, PixelPoint? at = null)
    {
        if (_vm is null || _services is null || !_tabs.ContainsKey(tab) || _windowId != 0) return null;
        var strip = _services.GetRequiredService<EditorTabsControl>();
        strip.AttachWindow(_vm, Interlocked.Increment(ref s_lastWindowId));
        var window = new TabWindow(strip, _prefs);
        if (at is { } point)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = point;
        }
        if (TopLevel.GetTopLevel(this) is Window { } from)
        {
            window.Width = Math.Max(600, from.Bounds.Width * 0.8);
            window.Height = Math.Max(400, from.Bounds.Height * 0.8);
        }
        var title = _vm.WhenAnyValue(x => x.ActiveTab, x => x.ActiveTab!.FileName, (active, _) => active)
            .Subscribe(active => { if (active?.WindowId == strip._windowId) window.ShowTitle(active.FileName); });
        window.Closed += (_, _) => title.Dispose();
        HandOver(tab, strip);
        window.Show();
        _vm.ActiveTab = tab;
        return strip;
    }

    /// <summary>The other windows' tabs come back to this strip, and those windows close (before the main window asks about unsaved tabs).</summary>
    internal void GatherOtherWindows()
    {
        foreach (var strip in Strips.Where(s => !ReferenceEquals(s, this)).ToList())
            strip.ReturnToMainWindow();
    }

    /// <summary>This window's tab goes back to the main window's strip, as it is; the window then closes.</summary>
    internal void ReturnToMainWindow()
    {
        if (Strips.FirstOrDefault(s => s._windowId == 0) is not { } main || ReferenceEquals(main, this)) return;
        foreach (var tab in _tabs.Keys.ToList()) HandOver(tab, main);
        if (_vm is not null && main.Shown is { } shown) _vm.ActiveTab = shown;
    }

    // A window of a tab of its own shows only its body: its strip's row of headers, with its buttons, is the main window's alone.
    private void ShowStripRow()
    {
        if (_stripRow is not null) _stripRow.IsVisible = _windowId == 0;
    }

    // A window of its own: shows only the tabs moved to it, and leaves the session and its restore to the main window's strip.
    private void AttachWindow(MainWindowViewModel vm, int windowId)
    {
        _windowId = windowId;
        ShowStripRow();
        Attach(vm);
    }

    // The header goes, the control moves to the other strip as it is (its editor, its output, its run).
    private void HandOver(EditorTabItemViewModel tab, EditorTabsControl target)
    {
        if (!_tabs.Remove(tab, out var entry)) return;
        this.FindControl<TabControl>("FileTabControl")!.Items.Remove(entry.Tab);
        entry.Tab.Content = null;
        tab.WindowId = target._windowId;
        target.Adopt(tab, entry.Control);
        RaiseEmptiedIfNoTabs();
    }

    private void Adopt(EditorTabItemViewModel tab, EditorsTabItemControl control)
    {
        var tabControl = this.FindControl<TabControl>("FileTabControl")!;
        var position = _vm!.Tabs.TakeWhile(t => !ReferenceEquals(t, tab)).Count(_tabs.ContainsKey);
        var header = AddTabItem(control, tab, tabControl, position);
        WatchDrags(header);
        tab.MoveToNewWindow = () => MoveToNewWindow(tab);
        _tabs[tab] = (header, control);
        _shown = tab;
        tabControl.SelectedItem = header;
    }

    private void RaiseEmptiedIfNoTabs()
    {
        if (_windowId != 0 && _tabs.Count == 0 && _creating.Count == 0) Emptied?.Invoke();
    }
}
