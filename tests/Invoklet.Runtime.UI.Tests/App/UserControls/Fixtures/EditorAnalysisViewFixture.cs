using Avalonia.Controls;
using Avalonia.VisualTree;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Avalonia.Themes.Scrolling;
using Invoklet.Contracts.IntelliSense;
using Invoklet.UserControls;
using Moq;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;

// An editor in a window, holding a short script, and an analysis service whose events a test raises.
internal sealed class EditorAnalysisViewFixture : IDisposable
{
    public const string Text = "var first = 1;\nvar second = first;\n";

    private readonly Window _window;

    public EditorAnalysisViewFixture()
    {
        Editor = new CodeTextEditor { Text = Text };
        _window = new Window { Width = 600, Height = 400, Content = Editor };
        _window.Show();
        _window.UpdateLayout();
    }

    public CodeTextEditor Editor { get; }
    public Mock<IIntelliSenseService> Service { get; } = new();
    public List<IReadOnlyList<ICodeFixInfo>> FixesHandedOn { get; } = [];

    public EditorAnalysisView CreateSut() => new(Editor, Service.Object, isDark: true, inlayHints: true, FixesHandedOn.Add);

    public static IDiagnosticInfo Diagnostic(int start, int end) =>
        Mock.Of<IDiagnosticInfo>(d => d.StartOffset == start && d.EndOffset == end && d.Severity == DiagnosticSeverity.Error && d.Message == "boom");

    public static ISemanticToken Token(int start, int length) =>
        Mock.Of<ISemanticToken>(t => t.StartOffset == start && t.Length == length && t.Kind == SemanticTokenKind.LocalName);

    public static IInlayHint Hint(int offset) => Mock.Of<IInlayHint>(h => h.Offset == offset && h.Text == ": int");

    public static ICodeFixInfo Fix() => Mock.Of<ICodeFixInfo>(f => f.Line == 1 && f.Title == "Fix it" && f.Key == "k");

    public IReadOnlyList<ScrollMarkerRow> ScrollMarks()
    {
        _window.UpdateLayout();
        return Editor.GetVisualDescendants().OfType<ScrollMarkerStrip>().Single().Rows;
    }

    public void Dispose()
    {
        _window.Close();
        Editor.Dispose();
    }
}
