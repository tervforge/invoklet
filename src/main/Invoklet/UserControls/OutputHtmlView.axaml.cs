using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Invoklet.Avalonia.HtmlRenderer;
using Invoklet.Avalonia.HtmlRenderer.External.Core.Entities;
using Invoklet.Services;
using Invoklet.UserControls.Models;

namespace Invoklet.UserControls;

// Hosts the script's Results output — binds to an OutputHtmlViewModel set as its
// DataContext (see EditorsTabItemControl's constructor: ResultsOutput.DataContext = State.Output)
// rather than being pushed content imperatively. Assembling Css/Content into the actual HTML
// document (<html><head><style>...</style></head><body>...</body></html>) and registering the
// custom output font are both a concern of this control, not the editor tab.
public partial class OutputHtmlView : UserControl
{
    private OutputHtmlViewModel? _boundModel;
    private bool _applyScheduled;
    private bool _scrollToEndPending;

    public OutputHtmlView()
    {
        InitializeComponent();

        OutputHtml.Container.AddFontFamily(
            new FontFamily(new Uri("avares://Invoklet/Assets/fonts/CustomFont.ttf"), "1 Smoothy DNA"));

        // No auto-scroll during streaming output — HtmlPanel's own TextProperty change
        // handler resets the scroll offset to (0,0) on every apply (see its OnPropertyChanged), so a
        // long-running script's output actually snapped back to the TOP on every appended line rather
        // than just "not scrolling". LayoutUpdated fires once the new (taller) content has actually
        // been measured/arranged — ScrollToEnd's own doc comment explains why it can't just be called
        // synchronously right after setting Text. _scrollToEndPending gates this to at most once per
        // ApplyPendingContent call, consumed by the very next layout pass that follows it.
        OutputHtml.LayoutUpdated += (_, _) =>
        {
            if (_scrollToRestore is { } offset)
            {
                _scrollToRestore = null;
                OutputHtml.ScrollToPoint(offset.X, offset.Y);
                PlaceFocusRing(false);
                return;
            }
            if (!_scrollToEndPending) return;
            _scrollToEndPending = false;
            OutputHtml.ScrollToEnd();
        };

        OutputHtml.LinkClicked += OnLinkClicked;
        OutputHtml.AddHandler(KeyDownEvent, OnKeyDownInPanel, RoutingStrategies.Tunnel);
        OutputHtml.AddHandler(PointerPressedEvent, (_, _) => MoveKeyboardFocus(null), RoutingStrategies.Tunnel);
        OutputHtml.PointerWheelChanged += (_, _) => Dispatcher.UIThread.Post(() => PlaceFocusRing(false));
        OutputHtml.LostFocus += (_, _) => KeyboardFocusRing.IsVisible = false;
        OutputHtml.GotFocus += (_, _) => PlaceFocusRing(false);
        OutputHtml.Container.ExtraContextMenuItems.Add(("Save results as HTML...", () => SaveHtmlRequested?.Invoke()));

        DataContextChanged += (_, _) => Rebind();
        // DataContext (State.Output, EditorTabItemControl.cs) is set exactly once per tab and never
        // reassigned — but switching tabs (either the outer file tabs or this control's own
        // Results/Debugger tabs, EditorTabItemControl.axaml) detaches this control from the visual
        // tree without ever touching DataContext. Without also rebinding here, a script that keeps
        // running while its tab isn't the visible one writes into a model nobody's listening to
        // anymore — output silently stops updating from the first tab switch onward (live bug
        // report: "the output in results periodically disappears if ... switching tabs").
        AttachedToVisualTree += (_, _) => Rebind();
        DetachedFromVisualTree += (_, _) => Unbind();
    }

    private void Rebind()
    {
        Unbind();

        if (DataContext is not OutputHtmlViewModel vm) return;

        _boundModel = vm;
        vm.PropertyChanged += OnModelPropertyChanged;

        // Plain PropertyChanged subscriptions don't replay on subscribe (unlike WhenAnyValue, used
        // here before) — schedule explicitly so binding to a freshly-created
        // OutputHtmlViewModel still shows its current state without a separate "push the starting
        // state" call.
        ScheduleApply();
    }

    private void Unbind()
    {
        if (_boundModel is not null) _boundModel.PropertyChanged -= OnModelPropertyChanged;
        _boundModel = null;
    }

    // Deliberately not evaluating e.PropertyName — Css and Content are the only two properties on
    // OutputHtmlViewModel, and both should trigger the same reapply. Crucially, this never touches
    // vm.Content itself (that would call OutputHtmlViewModel's ToString() on every single Append,
    // right back to the O(n²) cost this avoids) — only ApplyPendingContent reads it,
    // and only once per coalesced burst.
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => ScheduleApply();

    // Content changes once per output line while a script is running (EditorTabItemControl's
    // AppendOutput sets State.Output.Content on every single line) — applying every single change
    // synchronously forces a full HTML reparse + layout + paint per call, which freezes the UI
    // thread on a script that dumps hundreds of lines in a tight loop. Instead this only ever
    // schedules at most one Background-priority apply: AppendOutput's writes happen on the
    // dispatcher's default priority, so while a burst of them is still queued the apply keeps
    // getting pre-empted, and only actually runs once the burst has drained — reading whatever the
    // model's latest Css/Content is at that point, not every intermediate state.
    private void ScheduleApply()
    {
        if (_applyScheduled) return;
        _applyScheduled = true;
        Dispatcher.Post(ApplyPendingContent, DispatcherPriority.Background);
    }

    private void ApplyPendingContent()
    {
        _applyScheduled = false;
        if (_boundModel is not { } vm) return;

        ApplyCount++;
        // New output does not take the reader away from what they are selecting.
        if (!string.IsNullOrEmpty(OutputHtml.SelectedText))
        {
            var offset = OutputHtml.Container.ScrollOffset;
            _scrollToRestore = new Point(-offset.X, -offset.Y);
        }
        else
        {
            _scrollToEndPending = true;
        }
        OutputHtml.Text = PageOf(vm);
    }

    // Older output, kept from a version before the boxes, has tables without them.
    private string PageOf(OutputHtmlViewModel vm)
    {
        var content = vm.Content;
        var body = ResultsBoxes.Show(content, _toggledBoxes, ShowGeneratedLabels);
        if (!ResultsBoxes.AnyIn(content)) body = ResultsTables.WithMenus(body);
        return $"""
                <html>
                <head><style>{vm.Css}</style></head>
                <body>{body}</body>
                </html>
                """;
    }

    // Folding a box or turning the labels shows the same results: the reader stays where they are.
    private void ShowAgainInPlace()
    {
        if (_boundModel is not { } vm) return;
        var offset = OutputHtml.Container.ScrollOffset;
        _scrollToRestore = new Point(-offset.X, -offset.Y);
        ApplyCount++;
        OutputHtml.Text = PageOf(vm);
    }

    /// <summary>Whether a label made from a script's source text shows; one the script gave always does.</summary>
    internal bool ShowGeneratedLabels
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            ShowAgainInPlace();
        }
    }

    /// <summary>"Save results as HTML..." was chosen in the context menu.</summary>
    public event Action? SaveHtmlRequested;

    /// <summary>The "…" above a table was clicked and "Export to CSV" chosen; the number of the table (see <see cref="ResultsTables"/>).</summary>
    public event Action<int>? ExportTableRequested;

    /// <summary>The "…" of a box was clicked and "Export to CSV" chosen; the name of the box (see <see cref="ResultsBoxes"/>).</summary>
    public event Action<string>? ExportBoxRequested;

    private void OnLinkClicked(object? sender, HtmlRendererRoutedEventArgs<HtmlLinkClickedEventArgs> e)
    {
        if (e.Event is { } link && Activate(link.Link, atPointer: true)) link.Handled = true;
    }

    // What a click on a link does, and Enter on it. False for a link of no table.
    private bool Activate(string link, bool atPointer)
    {
        if (ResultsBoxes.ToggleOf(link) is { } toggle)
        {
            if (!_toggledBoxes.Remove(toggle)) _toggledBoxes.Add(toggle);
            ShowAgainInPlace();
            PlaceFocusRing(false);
        }
        else if (ResultsBoxes.TableLinkOf(link) is { } box)
        {
            ShowExportMenu(() => ExportBoxRequested?.Invoke(box), atPointer);
        }
        else if (ResultsTables.TableOf(link) is { } table)
        {
            ShowExportMenu(() => ExportTableRequested?.Invoke(table), atPointer);
        }
        else
        {
            return false;
        }
        return true;
    }

    private void ShowExportMenu(Action export, bool atPointer)
    {
        var item = new MenuItem { Header = "Export to CSV..." };
        item.Click += (_, _) => export();
        new MenuFlyout { Items = { item } }.ShowAt(atPointer ? OutputHtml : KeyboardFocusRing, showAtPointer: atPointer);
    }

    private void OnKeyDownInPanel(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift:
                var next = ResultsBoxes.NextLink(OutputHtml.Container.GetLinks().Select(l => l.Href), _keyboardLink, e.KeyModifiers == KeyModifiers.None);
                MoveKeyboardFocus(next);
                // Past the last one the key leaves the panel, as it would any control.
                e.Handled = next is not null;
                break;
            case Key.Enter or Key.Space when _keyboardLink is { } link && e.KeyModifiers == KeyModifiers.None:
                e.Handled = Activate(link, atPointer: false);
                break;
            default:
                // The panel scrolls on these keys, after this.
                if (_keyboardLink is not null) Dispatcher.UIThread.Post(() => PlaceFocusRing(false));
                break;
        }
    }

    private void MoveKeyboardFocus(string? link)
    {
        _keyboardLink = link;
        AutomationProperties.SetName(OutputHtml, link is null ? "Results"
            : ResultsBoxes.ToggleOf(link) is not null ? "Results: fold or open this table" : "Results: table actions");
        PlaceFocusRing(true);
    }

    // The ring on the link the keyboard is on, wherever the page lies now. A link the page no longer has takes the ring away.
    private void PlaceFocusRing(bool bringIntoView)
    {
        var target = _keyboardLink is null ? null : OutputHtml.Container.GetLinks().FirstOrDefault(l => l.Href == _keyboardLink);
        if (target is null || !OutputHtml.IsFocused)
        {
            if (target is null) _keyboardLink = null;
            KeyboardFocusRing.IsVisible = false;
            return;
        }

        var rect = target.Rectangle;
        var scroll = OutputHtml.Container.ScrollOffset;
        if (bringIntoView && (rect.Top + scroll.Y < 0 || rect.Bottom + scroll.Y > OutputHtml.Bounds.Height))
        {
            OutputHtml.ScrollToPoint(-scroll.X, Math.Max(0, rect.Top - 8));
            scroll = OutputHtml.Container.ScrollOffset;
        }
        KeyboardFocusRing.Margin = new Thickness(rect.Left + scroll.X - 2, rect.Top + scroll.Y - 2, 0, 0);
        KeyboardFocusRing.Width = rect.Width + 4;
        KeyboardFocusRing.Height = rect.Height + 4;
        KeyboardFocusRing.IsVisible = true;
    }

    private string? _keyboardLink;

    internal bool KeyboardRingShownForTests => KeyboardFocusRing.IsVisible;
    internal string? KeyboardLinkForTests => _keyboardLink;

    // Names of boxes are not reused by a later run, so what is here for an earlier one does no harm.
    private readonly HashSet<string> _toggledBoxes = [];
    private Point? _scrollToRestore;

    internal string? RenderedHtml => OutputHtml.Text;

    internal IReadOnlyList<(string Text, Action Click)> ContextMenuItemsForTests => OutputHtml.Container.ExtraContextMenuItems.ToList();

    // Test-only observability for the coalescing above — counts real applies to the underlying
    // HtmlPanel, as opposed to how many times the bound model's Css/Content actually changed.
    internal int ApplyCount { get; private set; }

    // Test-only observability (InternalsVisibleTo) for the auto-scroll fix — negative
    // Y means scrolled down from the top (HtmlContainer.ScrollOffset's own convention, mirrored by
    // HtmlPanel.ScrollToPoint: ScrollOffset = (-horizontalValue, -verticalValue)).
    internal Point ScrollOffsetForTests => OutputHtml.Container.ScrollOffset;
}
