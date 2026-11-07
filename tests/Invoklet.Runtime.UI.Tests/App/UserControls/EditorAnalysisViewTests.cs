using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Invoklet.UserControls;
using Xunit;
using static Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures.EditorAnalysisViewFixture;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// The editor's view of the background analysis: a result for the text it shows is drawn, one for another text is dropped.
public sealed class EditorAnalysisViewTests
{
    [AvaloniaFact]
    public void DiagnosticsChanged_ForTheShownText_UnderlinesAndMarksThem()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();

        // Act
        fixture.Service.Raise(s => s.DiagnosticsChanged += null, Text, (IReadOnlyList<IDiagnosticInfo>)[Diagnostic(4, 9)]);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(4, Assert.Single(sut.Diagnostics).StartOffset);
        Assert.Single(fixture.ScrollMarks());
    }

    [AvaloniaFact]
    public void DiagnosticsChanged_ForAnOlderText_IsDropped()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();

        // Act
        fixture.Service.Raise(s => s.DiagnosticsChanged += null, "var first = 0;", (IReadOnlyList<IDiagnosticInfo>)[Diagnostic(4, 9)]);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Empty(sut.Diagnostics);
        Assert.Empty(fixture.ScrollMarks());
    }

    [AvaloniaFact]
    public void SemanticTokensChanged_ForTheShownText_ColoursThem()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();

        // Act
        fixture.Service.Raise(s => s.SemanticTokensChanged += null, Text, (IReadOnlyList<ISemanticToken>)[Token(4, 5)]);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal("first", fixture.Editor.Document.GetText(Assert.Single(sut.SemanticHighlighter.Tokens)));
        Assert.Equal(1, sut.RedrawsRun);
    }

    [AvaloniaFact]
    public void InlayHintsChanged_ForTheShownText_RedrawsTheirLines()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();

        // Act
        fixture.Service.Raise(s => s.InlayHintsChanged += null, Text, (IReadOnlyList<IInlayHint>)[Hint(9)]);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(1, sut.RedrawsRun);
    }

    [AvaloniaFact]
    public void CodeFixesChanged_ForTheShownText_HandsThemOn()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();
        var fix = Fix();

        // Act
        fixture.Service.Raise(s => s.CodeFixesChanged += null, Text, (IReadOnlyList<ICodeFixInfo>)[fix]);
        fixture.Service.Raise(s => s.CodeFixesChanged += null, "older text", (IReadOnlyList<ICodeFixInfo>)[Fix()]);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Same(fix, Assert.Single(Assert.Single(fixture.FixesHandedOn)));
    }

    [AvaloniaFact]
    public void Clear_AfterAnAnalysis_LeavesNothingOfIt()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        using var sut = fixture.CreateSut();
        fixture.Service.Raise(s => s.DiagnosticsChanged += null, Text, (IReadOnlyList<IDiagnosticInfo>)[Diagnostic(4, 9)]);
        fixture.Service.Raise(s => s.SemanticTokensChanged += null, Text, (IReadOnlyList<ISemanticToken>)[Token(4, 5)]);
        Dispatcher.UIThread.RunJobs();

        // Act
        sut.Clear();

        // Assert
        Assert.Empty(sut.Diagnostics);
        Assert.Empty(sut.SemanticHighlighter.Tokens);
        Assert.Empty(fixture.ScrollMarks());
    }

    [AvaloniaFact]
    public void Dispose_BeforeAPostedResult_LeavesTheEditorBare()
    {
        // Arrange
        using var fixture = new EditorAnalysisViewFixture();
        var sut = fixture.CreateSut();
        fixture.Service.Raise(s => s.DiagnosticsChanged += null, Text, (IReadOnlyList<IDiagnosticInfo>)[Diagnostic(4, 9)]);

        // Act
        sut.Dispose();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Empty(sut.Diagnostics);
        Assert.DoesNotContain(fixture.Editor.TextArea.TextView.LineTransformers, t => ReferenceEquals(t, sut.SemanticHighlighter));
        fixture.Service.Raise(s => s.CodeFixesChanged += null, Text, (IReadOnlyList<ICodeFixInfo>)[Fix()]);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.FixesHandedOn);
    }
}
