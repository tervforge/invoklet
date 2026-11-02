using System.Reflection;
using Invoklet.Runtime;
using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public sealed class ResultsTablesTests
{
    private sealed record Person(string Name, int Age, bool Active, string? Note);

    // The markup a script's .Dump() really writes; the renderer is internal to the library the scripts reference.
    private static readonly MethodInfo Render = typeof(ObjectExtensions).Assembly.GetType("Invoklet.Runtime.DumpRenderer")!
        .GetMethod("Render", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(object), typeof(string), typeof(int?), typeof(int?)])!;

    private static string Dump(object value) => (string)Render.Invoke(null, [value, null, null, null])!;

    private static string[][] Rows(ResultsTable table) => [.. table.Rows.Select(r => r.ToArray())];

    [Fact]
    public void Parse_AListOfObjects_IsOneTableWithItsHeaderAndTheTextOfItsCells()
    {
        // Arrange
        var html = Dump(new[] { new Person("Ann", 31, true, null), new Person("Bob", 7, false, "x") });

        // Act
        var table = Assert.Single(ResultsTables.Parse(html));

        // Assert
        Assert.Equal([["Name", "Age", "Active", "Note"], ["Ann", "31", "true", ""], ["Bob", "7", "false", "x"]], Rows(table));
    }

    [Fact]
    public void Parse_ATableInACell_IsTheTextOfThatCell()
    {
        // Arrange
        var html = Dump(new[] { new { Name = "Ann", Tags = new[] { "a", "b" } } });

        // Act
        var table = Assert.Single(ResultsTables.Parse(html));

        // Assert
        Assert.Equal([["Name", "Tags"], ["Ann", "# Value ; 0 \"a\" ; 1 \"b\""]], Rows(table));
    }

    [Fact]
    public void Parse_SeveralTables_AreNumberedInTheOrderTheyAppear()
    {
        // Arrange
        var html = "<div>text</div>" + Dump(new[] { 1, 2 }) + Dump(new Dictionary<string, int> { ["a"] = 1 });

        // Act
        var tables = ResultsTables.Parse(html);

        // Assert
        Assert.Equal(2, tables.Count);
        Assert.Equal(["#", "Value"], tables[0].Rows[0]);
        Assert.Equal(["Key", "Value"], tables[1].Rows[0]);
    }

    [Fact]
    public void Parse_AHeaderRepeatedInTheTable_IsKeptOnce()
    {
        // Arrange
        var html = "<table><tr><th>A</th></tr><tr><td>1</td></tr><tr><th>A</th></tr><tr><td>2</td></tr></table>";

        // Act
        var table = Assert.Single(ResultsTables.Parse(html));

        // Assert
        Assert.Equal(["A", "1", "2"], table.Rows.Select(r => r[0]));
    }

    [Fact]
    public void Parse_AnEmptyTable_StillTakesItsNumber()
    {
        // Act
        var tables = ResultsTables.Parse("<table></table><table><tr><td>x</td></tr></table>");

        // Assert
        Assert.Equal([0, 1], tables.Select(t => t.Rows.Count));
    }

    [Fact]
    public void WithMenus_EveryTableNotInsideAnother_GetsALinkCarryingItsNumber()
    {
        // Arrange
        var html = "a" + Dump(new[] { new { Tags = new[] { "x" } } }) + "b" + Dump(new[] { 1 });

        // Act
        var page = ResultsTables.WithMenus(html);

        // Assert
        Assert.Equal(2, page.Split("class=\"dp-tablemenu\"").Length - 1);
        Assert.Contains("href=\"invoklet-table:0\"", page);
        Assert.Contains("href=\"invoklet-table:1\"", page);
        Assert.Equal(html, System.Text.RegularExpressions.Regex.Replace(page, "<div class=\"dp-tablemenu\">.*?</div>", ""));
    }

    [Fact]
    public void WithMenus_NoTables_IsTheSamePage()
    {
        Assert.Equal("<div>hello</div>", ResultsTables.WithMenus("<div>hello</div>"));
    }

    [Theory]
    [InlineData("invoklet-table:3", 3)]
    [InlineData("invoklet-table:x", null)]
    [InlineData("https://example.com", null)]
    public void TableOf_ALink_IsTheTableItStandsForOrNull(string link, int? expected) => Assert.Equal(expected, ResultsTables.TableOf(link));
}
