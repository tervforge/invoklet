using AvaloniaEdit.Document;
using Invoklet.Avalonia.Themes.Scrolling;
using Invoklet.Contracts.IntelliSense;
using Invoklet.UserControls;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.UserControls;

[Trait("Platform", "Independent")]
public sealed class DiagnosticScrollMarkersTests
{
    private static IDiagnosticInfo Diagnostic(int start, DiagnosticSeverity severity, string message = "m") =>
        Mock.Of<IDiagnosticInfo>(d => d.StartOffset == start && d.Severity == severity && d.Message == message);

    [Theory]
    [InlineData(DiagnosticSeverity.Error, ScrollMarkerKind.Error)]
    [InlineData(DiagnosticSeverity.Warning, ScrollMarkerKind.Warning)]
    [InlineData(DiagnosticSeverity.Info, ScrollMarkerKind.Info)]
    public void Of_ADiagnostic_TakesItsLineKindAndMessage(DiagnosticSeverity severity, ScrollMarkerKind expected)
    {
        // Arrange
        var document = new TextDocument("one\ntwo\nthree");

        // Act
        var marker = Assert.Single(DiagnosticScrollMarkers.Of(document, [Diagnostic(5, severity, "boom")]));

        // Assert
        Assert.Equal(new ScrollMarker(2, expected, "boom"), marker);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1000, 3)]
    public void Of_AnOffsetOutsideTheText_TakesTheNearestLine(int offset, int expectedLine)
    {
        // Arrange: the text may have changed since the analysis
        var document = new TextDocument("one\ntwo\nthree");

        // Act
        var marker = Assert.Single(DiagnosticScrollMarkers.Of(document, [Diagnostic(offset, DiagnosticSeverity.Error)]));

        // Assert
        Assert.Equal(expectedLine, marker.Line);
    }
}
