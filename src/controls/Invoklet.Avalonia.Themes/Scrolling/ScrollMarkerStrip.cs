using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Invoklet.Avalonia.Themes.Scrolling;

/// <summary>Marks for important lines at the edge of a scrollable view. It knows lines and kinds only; whoever owns the view gives it a
/// snapshot and goes to the line it is told. It lies under the view, so that the translucent scroll bar is drawn over the marks; the
/// pointer reaches them through <see cref="AttachInput"/>, which leaves the thumb to the scroll bar.</summary>
public sealed class ScrollMarkerStrip : Control
{
    private const string DefaultName = "Scroll markers";

    private IReadOnlyList<ScrollMarker> _markers = [];
    private ScrollMarkerGeometry? _geometry;
    private int _selected = -1;

    public ScrollMarkerStrip()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, DefaultName);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The user chose a mark: go to this line.</summary>
    public event Action<int>? LineActivated;

    public IReadOnlyList<ScrollMarkerRow> Rows { get; private set; } = [];

    public void Update(IReadOnlyList<ScrollMarker> markers, ScrollMarkerGeometry geometry)
    {
        _markers = markers;
        _geometry = geometry;
        Rebuild();
    }

    /// <summary>Takes clicks on the marks and shows their tooltips, from the control that lies over the strip.</summary>
    public void AttachInput(Control host)
    {
        host.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || MarkUnder(e) is not { } row) return;
            LineActivated?.Invoke(row.Line);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        host.AddHandler(PointerMovedEvent, (_, e) =>
            ToolTip.SetTip(host, MarkUnder(e) is { } row ? ScrollMarkerLayout.Describe(row) : null), RoutingStrategies.Tunnel);
    }

    private ScrollMarkerRow? MarkUnder(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (point.X < 0 || point.X > Bounds.Width) return null;
        // The thumb stays draggable where it lies over a mark.
        if (e.Source is Visual source && (source is Thumb || source.FindAncestorOfType<Thumb>() is not null)) return null;
        return RowAt(point.Y);
    }

    /// <summary>The mark at a height in the strip, if any.</summary>
    public ScrollMarkerRow? RowAt(double y) => Rows.FirstOrDefault(r => y >= r.Top && y <= r.Top + r.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Rebuild();
    }

    private void Rebuild()
    {
        Rows = _geometry is null ? [] : ScrollMarkerLayout.Rows(_markers, _geometry, Bounds.Height);
        Focusable = Rows.Count > 0;
        Select(_selected >= Rows.Count ? Rows.Count - 1 : _selected);
        InvalidateVisual();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_selected < 0 && Rows.Count > 0) Select(0);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Rows.Count == 0) return;
        switch (e.Key)
        {
            case Key.Up:
                Select(Math.Max(0, _selected - 1));
                break;
            case Key.Down:
                Select(Math.Min(Rows.Count - 1, _selected + 1));
                break;
            case Key.Enter or Key.Space when _selected >= 0:
                LineActivated?.Invoke(Rows[_selected].Line);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Select(int index)
    {
        _selected = index;
        var text = index >= 0 ? ScrollMarkerLayout.Describe(Rows[index]) : null;
        AutomationProperties.SetName(this, text ?? DefaultName);
        if (!IsFocused) return;
        ToolTip.SetTip(this, text);
        ToolTip.SetIsOpen(this, text is not null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var bar = new Rect(0, row.Top, width, row.Height);
            // Errors, warnings and information are one size; the tooltip tells them in words.
            switch (row.Kind)
            {
                case ScrollMarkerKind.Error:
                    context.FillRectangle(Brush("Invoklet.Diagnostic.ErrorBrush"), bar);
                    break;
                case ScrollMarkerKind.Warning:
                    context.FillRectangle(Brush("Invoklet.Diagnostic.WarningBrush"), bar);
                    break;
                case ScrollMarkerKind.Info:
                    context.FillRectangle(Brush("Invoklet.Diagnostic.InfoBrush"), bar);
                    break;
                case ScrollMarkerKind.Breakpoint:
                    context.DrawEllipse(Brush("Invoklet.Margin.BreakpointBrush"), null, bar.Center, 3, 3);
                    break;
                case ScrollMarkerKind.Execution:
                    context.FillRectangle(Brush("Invoklet.Margin.DebugArrowBrush"), bar);
                    break;
            }
            if (i == _selected && IsFocused)
                context.DrawRectangle(new Pen(Brush("Invoklet.Focus.BorderBrush")), bar);
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
