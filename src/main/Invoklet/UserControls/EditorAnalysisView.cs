using Avalonia.Threading;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Services;

namespace Invoklet.UserControls;

/// <summary>
/// What the background analysis says about the text, drawn in the editor: the underlines and scroll marks of the diagnostics, the
/// semantic colours and the inlay hints. The code fixes are handed on, for the lightbulb. A result for a text the editor no longer
/// holds is dropped; the decorations follow the edits of one document, and a new one starts bare until its first analysis.
/// </summary>
internal sealed class EditorAnalysisView : IDisposable
{
    private readonly CodeTextEditor _editor;
    private readonly SquiggleRenderer _squiggles = new();
    private readonly InlayHintRenderer _inlayHints;
    private readonly RedrawCoalescer _redraws;
    private readonly IntelliSenseEventSubscriptions _subscriptions;
    private bool _disposed;

    public EditorAnalysisView(CodeTextEditor editor, IIntelliSenseService service, bool isDark, bool inlayHints,
        Action<IReadOnlyList<ICodeFixInfo>> codeFixesChanged)
    {
        _editor = editor;
        SemanticHighlighter.SetDark(isDark);
        _inlayHints = new InlayHintRenderer { IsEnabled = inlayHints };
        var view = editor.TextArea.TextView;
        view.BackgroundRenderers.Add(_squiggles);
        view.LineTransformers.Add(SemanticHighlighter);
        view.ElementGenerators.Add(_inlayHints);
        _redraws = new RedrawCoalescer(
            work => Dispatcher.UIThread.Post(work, DispatcherPriority.Background),
            () => _disposed ? null : editor.Document?.TextLength,
            (start, length) => view.Redraw(start, length));

        _subscriptions = new IntelliSenseEventSubscriptions(
            service,
            (code, diagnostics) => ForTheShownText(code, () => ShowDiagnostics(diagnostics)),
            (code, tokens) => ForTheShownText(code, () => _redraws.Request(SemanticHighlighter.Update(editor.Document, tokens))),
            (code, fixes) => ForTheShownText(code, () => codeFixesChanged(fixes)),
            (code, hints) => ForTheShownText(code, () => _redraws.Request(_inlayHints.Update(editor.Document, hints))));
        _subscriptions.Attach();
    }

    /// <summary>The diagnostics at their current, edit-adjusted offsets.</summary>
    public IReadOnlyList<IDiagnosticInfo> Diagnostics => _squiggles.Diagnostics;

    public SemanticHighlighter SemanticHighlighter { get; } = new();

    /// <summary>The partial redraws run so far, coalesced.</summary>
    public int RedrawsRun => _redraws.RedrawsRun;

    public void SetDark(bool isDark) => SemanticHighlighter.SetDark(isDark);

    public void SetInlayHintsEnabled(bool enabled)
    {
        if (_inlayHints.IsEnabled == enabled) return;
        _inlayHints.IsEnabled = enabled;
        _editor.TextArea.TextView.Redraw();
    }

    /// <summary>The editor holds another document: nothing of the old one's analysis applies to it.</summary>
    public void Clear()
    {
        _squiggles.Update(_editor.Document, []);
        _editor.UpdateScrollMarkers([]);
        SemanticHighlighter.Update(_editor.Document, []);
        _inlayHints.Update(_editor.Document, []);
    }

    // The analysis reports on its own thread, for the text it read: a result reaches the editor only while that text is still shown.
    private void ForTheShownText(string code, Action apply) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (!ReferenceEquals(_editor.Text, code) && _editor.Text != code) return;
            apply();
        });

    private void ShowDiagnostics(IReadOnlyList<IDiagnosticInfo> diagnostics)
    {
        _squiggles.Update(_editor.Document, diagnostics);
        _editor.UpdateScrollMarkers(DiagnosticScrollMarkers.Of(_editor.Document, diagnostics));
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscriptions.Dispose();
        var view = _editor.TextArea.TextView;
        view.BackgroundRenderers.Remove(_squiggles);
        view.LineTransformers.Remove(SemanticHighlighter);
        view.ElementGenerators.Remove(_inlayHints);
    }
}
