using Invoklet.NonUI.Tests.App.Services.Fixtures;
using Invoklet.Services;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Platform", "Independent")]
public sealed class ResultsExporterTests
{
    [Fact]
    public async Task ExportAsync_ResultsOfASavedScript_AreOfferedUnderItsNameInItsFolderAndWrittenAsAPage()
    {
        // Arrange
        var fixture = new ResultsExporterFixture();
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowSaveHtmlFileDialogAsync("Report.html", "scripts"), Times.Once);
        var (path, text) = Assert.Single(fixture.Written);
        Assert.Equal("chosen.html", path);
        Assert.Contains("<div class=\"out\">hello</div>", text);
        Assert.Contains("body { color: red; }", text);
    }

    [Fact]
    public async Task ExportAsync_AScriptNeverSaved_IsOfferedAsResultsInTheStartFolder()
    {
        // Arrange
        var fixture = new ResultsExporterFixture { ScriptPath = null };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowSaveHtmlFileDialogAsync("Results.html", "documents"), Times.Once);
    }

    [Fact]
    public async Task ExportAsync_NoResults_SaysSoAndAsksNothing()
    {
        // Arrange
        var fixture = new ResultsExporterFixture { Content = "" };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowErrorAsync("Export Results", "There are no results to export."), Times.Once);
        fixture.Windows.Verify(w => w.ShowSaveHtmlFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Empty(fixture.Written);
    }

    [Fact]
    public async Task ExportAsync_TheDialogCancelled_WritesNothing()
    {
        // Arrange
        var fixture = new ResultsExporterFixture { ChosenPath = null };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(fixture.Written);
        fixture.Windows.Verify(w => w.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(typeof(IOException), "disk full")]
    [InlineData(typeof(UnauthorizedAccessException), "Access was denied.")]
    public async Task ExportAsync_TheFileCannotBeWritten_ReportsItWithThePathAndTheReason(Type failure, string reason)
    {
        // Arrange
        var fixture = new ResultsExporterFixture { WriteFails = (Exception)Activator.CreateInstance(failure, "disk full")! };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowErrorAsync("Export Results Failed", "Could not write 'chosen.html'.\n" + reason), Times.Once);
    }

    [Fact]
    public void Build_AStylesheetAndOutput_IsAOneUtf8PageWithAnEncodedTitle()
    {
        // Arrange / Act
        var page = ResultsHtml.Build("p { }", "<b>x</b>", "A & B <results>");

        // Assert
        Assert.StartsWith("<!DOCTYPE html>", page);
        Assert.Contains("<meta charset=\"utf-8\">", page);
        Assert.Contains("<title>A &amp; B &lt;results&gt;</title>", page);
        Assert.Contains("<style>\np { }\n</style>", page);
        Assert.Contains("<body>\n<b>x</b>\n</body>", page);
    }

    [Fact]
    public void Build_AStylesheetThatClosesItself_CannotLetTheRestBeReadAsMarkup()
    {
        // Arrange / Act
        var page = ResultsHtml.Build("a { } </STYLE><script>x()</script>", "", "t");

        // Assert
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "</style", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private const string TwoTables = "<table><tr><th>A</th></tr><tr><td>1</td></tr></table><table><tr><th>B</th></tr><tr><td>2, 3</td></tr></table>";

    [Fact]
    public async Task ExportBoxCsvAsync_AFoldedBox_WritesItsRows()
    {
        // Arrange
        var fixture = new ResultsExporterFixture
        {
            Content = """<div class="dp-box" data-dp-node="a" data-dp-collapsed="1"><table><tr><td>Ann</td></tr></table></div>""",
        };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportBoxCsvAsync("a", TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowSaveCsvFileDialogAsync("Report.csv", "scripts"), Times.Once);
        Assert.Contains("Ann", Assert.Single(fixture.WrittenCsv).Item2);
    }

    [Fact]
    public async Task ExportBoxCsvAsync_ABoxNoLongerThere_SaysSoAndWritesNothing()
    {
        // Arrange
        var fixture = new ResultsExporterFixture();
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportBoxCsvAsync("gone", TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowErrorAsync("Export Table", It.IsAny<string>()), Times.Once);
        Assert.Empty(fixture.WrittenCsv);
    }

    [Fact]
    public async Task ExportTableCsvAsync_TheSecondOfTwoTables_IsOfferedNumberedAndWrittenAsCsv()
    {
        // Arrange
        var fixture = new ResultsExporterFixture { Content = TwoTables };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportTableCsvAsync(1, TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowSaveCsvFileDialogAsync("Report-2.csv", "scripts"), Times.Once);
        var (path, text) = Assert.Single(fixture.WrittenCsv);
        Assert.Equal("chosen.csv", path);
        Assert.Equal("B\r\n\"2, 3\"\r\n", text);
    }

    [Fact]
    public async Task ExportTableCsvAsync_TheOnlyTable_IsOfferedUnderTheScriptsName()
    {
        // Arrange
        var fixture = new ResultsExporterFixture { Content = "<table><tr><td>x</td></tr></table>" };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportTableCsvAsync(0, TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowSaveCsvFileDialogAsync("Report.csv", "scripts"), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ExportTableCsvAsync_ATableThatIsNotThere_SaysSoAndAsksNothing(int table)
    {
        // Arrange
        var fixture = new ResultsExporterFixture { Content = TwoTables };
        var sut = fixture.CreateSut();

        // Act
        await sut.ExportTableCsvAsync(table, TestContext.Current.CancellationToken);

        // Assert
        fixture.Windows.Verify(w => w.ShowErrorAsync("Export Table", "This table is no longer in the results."), Times.Once);
        fixture.Windows.Verify(w => w.ShowSaveCsvFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExportTableCsvAsync_TheDialogCancelledOrTheFileUnwritable_WritesNothingAndReports()
    {
        // Arrange
        var cancelled = new ResultsExporterFixture { Content = TwoTables, ChosenCsvPath = null };
        var failing = new ResultsExporterFixture { Content = TwoTables, WriteFails = new IOException("disk full") };

        // Act
        await cancelled.CreateSut().ExportTableCsvAsync(0, TestContext.Current.CancellationToken);
        await failing.CreateSut().ExportTableCsvAsync(0, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(cancelled.WrittenCsv);
        failing.Windows.Verify(w => w.ShowErrorAsync("Export Results Failed", It.Is<string>(m => m.Contains("chosen.csv") && m.Contains("disk full"))), Times.Once);
    }
}
