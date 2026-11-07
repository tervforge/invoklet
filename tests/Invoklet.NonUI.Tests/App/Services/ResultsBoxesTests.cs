using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Platform", "Independent")]
public sealed class ResultsBoxesTests
{
    private static readonly HashSet<string> None = [];

    private const string Table = """<table class="dp-table"><thead><tr><th class="dp-th">Name</th></tr></thead><tbody><tr><td class="dp-td">Ann</td></tr></tbody></table>""";

    private static string Box(string id, string content, bool collapsed = false, string title = "P[] &mdash; 1 item") =>
        $"""<div class="dp-box" data-dp-node="{id}"{(collapsed ? " data-dp-collapsed=\"1\"" : "")}><div class="dp-count">{title}</div>{content}</div>""";

    [Fact]
    public void Show_AnOpenBox_HasItsTitleArrowMenuAndContent()
    {
        // Arrange
        var html = Box("a", Table);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.Contains("P[] &mdash; 1 item", page);
        Assert.Contains("href=\"invoklet-node:a\"", page);
        Assert.Contains("href=\"invoklet-box:a\"", page);
        Assert.Contains("&#9662;", page);
        Assert.Contains("Ann", page);
    }

    [Fact]
    public void Show_ABoxWrittenFolded_HidesItsContentAndPointsTheArrowOn()
    {
        // Arrange
        var html = Box("a", Table, collapsed: true);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.DoesNotContain("Ann", page);
        Assert.Contains("&#9656;", page);
        Assert.Contains("P[] &mdash; 1 item", page);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Show_ToggledBox_TurnsFromHowItWasWritten(bool collapsed, bool expectContent)
    {
        // Arrange
        var html = Box("a", Table, collapsed);

        // Act
        var page = ResultsBoxes.Show(html, new HashSet<string> { "a" }, generatedLabels: true);

        // Assert
        Assert.Equal(expectContent, page.Contains("Ann"));
    }

    [Fact]
    public void Show_OpeningOneNestedBox_LeavesTheOthersFolded()
    {
        // Arrange
        var html = Box("outer", Box("one", "<b>first</b>", collapsed: true) + Box("two", "<b>second</b>", collapsed: true));

        // Act
        var page = ResultsBoxes.Show(html, new HashSet<string> { "one" }, generatedLabels: true);

        // Assert
        Assert.Contains("first", page);
        Assert.DoesNotContain("second", page);
    }

    [Fact]
    public void Show_ALabelBeforeABox_StaysALineOfItsOwnAboveTheStrip()
    {
        // Arrange
        var html = """<div class="dp-label">Team</div>""" + Box("a", Table);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.StartsWith("""<div class="dp-label">Team</div><div class="dp-box">""", page);
        Assert.Contains("P[] &mdash; 1 item", page);
    }

    [Fact]
    public void Show_NoGeneratedLabels_LeavesOutTheExpressionAndKeepsTheHeadingAndTheType()
    {
        // Arrange
        var html = """<div class="dp-label dp-label-generated">people</div>""" + Box("a", Table)
            + """<div class="dp-label">Team</div>""" + Box("b", Table);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: false);

        // Assert
        Assert.DoesNotContain("people", page);
        Assert.Contains(">Team<", page);
        Assert.Contains("P[] &mdash; 1 item", page);
        Assert.Contains("Ann", page);
    }

    [Theory]
    [InlineData(true, "generated", "<div class=\"dp-dump\">")]
    [InlineData(false, "generated", "<div class=\"dp-dump-plain\">")]
    [InlineData(false, "given", "<div class=\"dp-dump\">")]
    [InlineData(false, "none", "<div class=\"dp-dump\">")]
    public void Show_ADumpBlock_LosesTheAccentBarOnlyWithItsGeneratedLabel(bool generatedLabels, string label, string expected)
    {
        // Arrange
        var heading = label switch
        {
            "generated" => """<div class="dp-label dp-label-generated">products</div>""",
            "given" => """<div class="dp-label">Team</div>""",
            _ => "",
        };
        var html = "<div class=\"dp-dump\">" + heading + Box("a", Table) + "</div>";

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels);

        // Assert
        Assert.StartsWith(expected, page);
    }

    [Fact]
    public void Show_GeneratedLabels_ShowTheExpression()
    {
        // Arrange
        var html = """<div class="dp-label dp-label-generated">people</div>""" + Box("a", Table);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.Contains(">people<", page);
    }

    [Fact]
    public void Show_ATable_HasItsTitleAsAFirstRowAsWideAsTheTable()
    {
        // Arrange
        var table = """<table class="dp-table"><thead><tr><th class="dp-th">A</th><th class="dp-th">B</th></tr></thead><tbody></tbody></table>""";

        // Act
        var page = ResultsBoxes.Show(Box("a", table), None, generatedLabels: true);

        // Assert
        Assert.StartsWith("""<div class="dp-box"><table class="dp-table dp-boxed"><thead><tr><td class="dp-boxstrip" colspan="2">""", page);
        Assert.Contains("""<th class="dp-th">B</th></tr></thead><tbody></tbody></table></div>""", page);
    }

    [Fact]
    public void Show_AFoldedTable_ClosesAfterItsTitleRow()
    {
        // Arrange
        var html = Box("a", Table, collapsed: true);

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.EndsWith("</td></tr></tbody></table></div>", page);
        Assert.Contains("""<table class="dp-table dp-boxed"><tbody><tr><td class="dp-boxstrip" colspan="1">""", page);
        Assert.DoesNotContain("dp-boxtitle-open", page);
    }

    [Fact]
    public void Show_AnOutermostBoxWithoutATitle_HasNoStripAndTheMenuInTheTableHeader()
    {
        // Arrange
        var html = """<div class="dp-box" data-dp-node="a" data-dp-collapsed="1">""" + Table + "</div>";

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.DoesNotContain("dp-boxtitle", page);
        Assert.Contains("href=\"invoklet-box:a\"", page);
        Assert.Contains("Ann", page);
    }

    [Fact]
    public void Show_ABoxWithoutATable_HasNoMenu()
    {
        // Arrange
        var html = Box("a", """<span class="dp-truncated">empty</span>""");

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.DoesNotContain("invoklet-box:", page);
        Assert.Contains("empty", page);
    }

    [Fact]
    public void Show_ADivOfTheScriptThatNeverCloses_KeepsTheRestOfThePage()
    {
        // Arrange
        var html = """<div class="dp-box" data-dp-node="a"><div>open <b>text</b>""";

        // Act
        var page = ResultsBoxes.Show(html, None, generatedLabels: true);

        // Assert
        Assert.Equal(html, page);
    }

    [Fact]
    public void TableOf_AFoldedBox_HasItsRows()
    {
        // Arrange
        var html = Box("a", Table, collapsed: true);

        // Act
        var table = ResultsBoxes.TableOf(html, "a");

        // Assert
        Assert.NotNull(table);
        Assert.Equal([["Name"], ["Ann"]], table.Rows.Select(r => r.ToArray()));
    }

    [Fact]
    public void TableOf_ANestedBox_IsItsOwnTable()
    {
        // Arrange
        var inner = """<table class="dp-table"><tr><td class="dp-td">inner</td></tr></table>""";
        var html = Box("outer", $"""<table class="dp-table"><tr><td class="dp-td">{Box("in", inner, collapsed: true)}</td></tr></table>""");

        // Act
        var table = ResultsBoxes.TableOf(html, "in");

        // Assert
        Assert.Equal("inner", Assert.Single(Assert.Single(table!.Rows)));
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("empty")]
    public void TableOf_NoSuchBoxOrNoTable_IsNull(string id)
    {
        // Arrange
        var html = Box("empty", "<span>none</span>");

        // Act
        var table = ResultsBoxes.TableOf(html, id);

        // Assert
        Assert.Null(table);
    }

    [Theory]
    [InlineData("invoklet-node:a", "a", null)]
    [InlineData("invoklet-box:b", null, "b")]
    [InlineData("invoklet-table:3", null, null)]
    [InlineData("https://example.com", null, null)]
    public void Links_OfAPage_TellWhatTheyStandFor(string link, string? toggle, string? table)
    {
        // Arrange (the link is the argument)

        // Act
        var (toggleOf, tableOf) = (ResultsBoxes.ToggleOf(link), ResultsBoxes.TableLinkOf(link));

        // Assert
        Assert.Equal(toggle, toggleOf);
        Assert.Equal(table, tableOf);
    }

    [Theory]
    [InlineData("""<table></table>""", false)]
    [InlineData("""<div class="dp-box" data-dp-node="a"></div>""", true)]
    public void AnyIn_OutputOfAnEarlierVersionOrThisOne_TellsThem(string html, bool expected)
    {
        // Arrange (the page is the argument)

        // Act
        var any = ResultsBoxes.AnyIn(html);

        // Assert
        Assert.Equal(expected, any);
    }
}
