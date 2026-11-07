using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts.Debugging;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Services;
using Invoklet.UserControls.Healpers;
using Invoklet.UserControls.Models;

namespace Invoklet.UserControls;

/// <summary>
/// What the editor shows for the symbol under the pointer, once the pointer stops: Quick Info or the message of a diagnostic in a
/// tooltip, and while the script is paused in the debugger the symbol's value in a tip of its own that the pointer can move into.
/// The decision is <see cref="HoverResolver"/>'s; this class owns the timing, the tip, the highlight of the symbol and their clean-up.
/// </summary>
internal sealed class EditorHover : IDisposable
{
    private readonly CodeTextEditor _editor;
    private readonly HoverResolver _resolver;
    private readonly EditorTimings _timings;
    private readonly Func<IBreakpointHitContext?> _debugContext;
    private readonly Func<IReadOnlyList<IDiagnosticInfo>> _diagnostics;
    private readonly Func<bool> _isDark;
    private readonly DebugHoverController _debugHover = new();

    // Not disposed when replaced: a superseded hover's background task may still be reading its token, and registering
    // against a disposed CancellationTokenSource throws. One whose WaitHandle is never touched holds nothing unmanaged.
    private CancellationTokenSource _quickInfoCts = new();
    private CancellationTokenSource? _closeCts;
    private bool _pointerInsideTip;
    private long _generation;
    private (int Start, int Length)? _valueSpan;
    private Popup? _valuePopup;
    private bool _disposed;

    public EditorHover(CodeTextEditor editor, IIntelliSenseService intelliSense, EditorTimings timings,
        Func<IBreakpointHitContext?> debugContext, Func<IReadOnlyList<IDiagnosticInfo>> diagnostics, Func<bool> isDark)
    {
        _editor = editor;
        _resolver = new HoverResolver(_debugHover, intelliSense);
        _timings = timings;
        _debugContext = debugContext;
        _diagnostics = diagnostics;
        _isDark = isDark;
        ToolTip.SetShowDelay(editor.TextArea.TextView, 0);
        editor.TextArea.TextView.PointerMoved += OnPointerMoved;
        editor.TextArea.TextView.PointerExited += OnPointerExited;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var editor = _editor;
        // An open value tip stays while the pointer is over its symbol, and is not replaced while the pointer crosses other text
        // on its way into the tip: it closes after the grace period unless the pointer gets into it or back onto the symbol.
        if (_valueSpan is { } span)
        {
            var hovered = editor.GetOffsetFromPoint(e.GetPosition(editor));
            if (hovered >= span.Start && hovered < span.Start + span.Length)
                _closeCts?.Cancel();
            else if (_closeCts is null || _closeCts.IsCancellationRequested)
                ScheduleClose();
            return;
        }

        _quickInfoCts.Cancel();
        _quickInfoCts = new CancellationTokenSource();
        _closeCts?.Cancel();
        _pointerInsideTip = false;
        _generation++;
        var ct = _quickInfoCts.Token;
        var (debugGeneration, debugToken) = _debugHover.Begin();
        var pos = e.GetPosition(editor);

        _ = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, debugToken);
            var requestCt = linked.Token;
            try
            {
                await Task.Delay(_timings.QuickInfoHoverDelay, requestCt);
                var (request, debugContext) = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var context = _debugContext();
                    return (new HoverRequest(editor.Text, editor.GetOffsetFromPoint(pos), editor.IdentifierChars, context,
                        DebugHoverController.Capture(context),
                        _diagnostics().Select(HoverDiagnostic.Of).ToArray(),
                        () => ReferenceEquals(context, _debugContext())), context);
                });
                if (request.Offset < 0 || ct.IsCancellationRequested) return;

                var hover = await _resolver.ResolveAsync(request, requestCt);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || ct.IsCancellationRequested || !_debugHover.IsCurrent(debugGeneration) ||
                        !ReferenceEquals(debugContext, _debugContext())) return;

                    if (hover is DebugValueHover value)
                    {
                        _valueSpan = (value.SymbolStart, value.Symbol.Length);
                        editor.SetHoverTokenHighlight(value.SymbolStart, value.Symbol.Length,
                            ThemeBrush("Invoklet.Editor.HoverHighlightBrush", new SolidColorBrush(Color.FromArgb(55, 90, 140, 210))));
                        ToolTip.SetTip(editor.TextArea.TextView, null);
                        ShowValuePopup(value.SymbolStart, BuildValueTip(value.Value, debugContext, debugToken, value.Symbol));
                        return;
                    }

                    // Built here, not in the resolver: DisplaySegmentRenderer creates Avalonia controls, which must happen on the UI
                    // thread, unlike the rest of this background task.
                    object? content = hover switch
                    {
                        DiagnosticHover diagnostic => diagnostic.Message,
                        QuickInfoHover quickInfo => DisplaySegmentRenderer.BuildWithSummary(
                            quickInfo.Info.Signature, quickInfo.Info.Summary, _isDark()),
                        _ => null,
                    };
                    ToolTip.SetTip(editor.TextArea.TextView, content);
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _quickInfoCts.Cancel();
        ScheduleClose();
    }

    /// <summary>Takes away what the hover shows: the debugger moved on, or the run ended.</summary>
    public void Invalidate()
    {
        _pointerInsideTip = false;
        _valueSpan = null;
        _generation++;
        _debugHover.Cancel();
        _editor.ClearHoverTokenHighlight();
        ToolTip.SetTip(_editor.TextArea.TextView, null);
        CloseValuePopup();
    }

    // Just below the hovered symbol's line, like the completion window.
    private void ShowValuePopup(int symbolStart, Control content)
    {
        CloseValuePopup();
        var textView = _editor.TextArea.TextView;
        var location = _editor.Document.GetLocation(symbolStart);
        var position = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineBottom)
            - textView.ScrollOffset;
        var popup = _valuePopup = new Popup
        {
            PlacementTarget = textView,
            InheritsTransform = true,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            HorizontalOffset = position.X,
            VerticalOffset = position.Y,
            IsLightDismissEnabled = false,
            Focusable = false,
            Child = content,
        };
        ((ISetLogicalParent)popup).SetParent(TopLevel.GetTopLevel(_editor));
        popup.IsOpen = true;
    }

    private void CloseValuePopup()
    {
        if (_valuePopup is not { } popup) return;
        popup.IsOpen = false;
        ((ISetLogicalParent)popup).SetParent(null);
        _valuePopup = null;
    }

    private void ScheduleClose()
    {
        _closeCts?.Cancel();
        var cts = _closeCts = new CancellationTokenSource();
        _ = CloseAfterGraceAsync(cts.Token, _generation, _debugContext());
    }

    private async Task CloseAfterGraceAsync(CancellationToken token, long generation, IBreakpointHitContext? context)
    {
        try
        {
            await Task.Delay(_timings.DebugTooltipCloseGrace, token);
            if (_disposed || token.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || _pointerInsideTip ||
                    generation != _generation || !ReferenceEquals(context, _debugContext())) return;
                Invalidate();
            });
        }
        catch (OperationCanceledException) { }
    }

    private DebugValueTip BuildValueTip(IVariableInfo root, IBreakpointHitContext? context, CancellationToken token, string symbol)
    {
        var tip = new DebugValueTip(CreateLocal(root, context, token, symbol));
        tip.PointerEntered += OnTipPointerEntered;
        tip.PointerExited += OnTipPointerExited;
        return tip;
    }

    // Like the Locals panel's rows, but members are fetched only while this tip's stop is current, at most MaxChildren of them and
    // within the child timeout.
    private DebugLocal CreateLocal(IVariableInfo variable, IBreakpointHitContext? context, CancellationToken token, string? name = null)
    {
        Func<Task<IReadOnlyList<DebugLocal>>>? loader = variable.HasChildren && context is not null
            ? async () =>
            {
                if (!ReferenceEquals(context, _debugContext())) return [];
                var children = await _debugHover.LoadChildrenAsync(context, variable, token);
                return children.Select(c => CreateLocal(c, context, token)).ToList();
            }
        : null;
        return new DebugLocal(string.IsNullOrEmpty(variable.Name) ? name ?? "" : variable.Name,
            variable.Value, variable.TypeName, loader is not null, loader);
    }

    private void OnTipPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_disposed) return;
        _pointerInsideTip = true;
        _closeCts?.Cancel();
    }

    private void OnTipPointerExited(object? sender, PointerEventArgs e)
    {
        if (_disposed) return;
        _pointerInsideTip = false;
        ScheduleClose();
    }

    private static IBrush ThemeBrush(string key, IBrush fallback)
    {
        var app = Application.Current;
        return app?.TryGetResource(key, app.ActualThemeVariant, out var value) == true
            && value is IBrush brush ? brush : fallback;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.TextArea.TextView.PointerMoved -= OnPointerMoved;
        _editor.TextArea.TextView.PointerExited -= OnPointerExited;
        _quickInfoCts.Cancel();
        _closeCts?.Cancel();
        CloseValuePopup();
        _debugHover.Dispose();
    }
}
