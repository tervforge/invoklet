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

    [Fact]
    public void Parse_AListOfObjects_IsOneSheetWithAHeaderAndTypedCells()
    {
        // Arrange
        var html = Dump(new[] { new Person("Ann", 31, true, null), new Person("Bob", 7, false, "x") });

        // Act
        var sheets = ResultsTables.Parse(html);

        // Assert
        var sheet = Assert.Single(sheets, s => s.Name == "Table 1");
        Assert.Equal(["Name", "Age", "Active", "Note"], sheet.Rows[0].Select(c => c.Text));
        Assert.All(sheet.Rows[0], c => Assert.Equal(CellKind.Header, c.Kind));
        Assert.Equal([("Ann", CellKind.Text), ("31", CellKind.Number), ("true", CellKind.Boolean), ("", CellKind.Text)], sheet.Rows[1].Select(c => (c.Text, c.Kind)));
        Assert.Equal("Bob", sheet.Rows[2][0].Text);
    }

    [Fact]
    public void Parse_ATableInACell_IsTheTextOfThatCell()
    {
        // Arrange
        var html = Dump(new[] { new { Name = "Ann", Tags = new[] { "a", "b" } } });

        // Act
        var sheets = ResultsTables.Parse(html);

        // Assert
        var sheet = Assert.Single(sheets);
        Assert.Equal(["Name", "Tags"], sheet.Rows[0].Select(c => c.Text));
        Assert.Equal("# Value ; 0 \"a\" ; 1 \"b\"", sheet.Rows[1][1].Text);
        Assert.Equal(2, sheet.Rows.Count);
    }

    [Fact]
    public void Parse_TextOutsideTables_IsTheLastSheetOneLineToARow()
    {
        // Arrange
        var html = "<div>first</div><div>second</div>" + Dump(new[] { 1, 2 });

        // Act
        var sheets = ResultsTables.Parse(html);

        // Assert
        Assert.Equal(["Table 1", "Output"], sheets.Select(s => s.Name));
        Assert.Equal(2, sheets[1].Rows.Count);
        Assert.Contains("first", sheets[1].Rows[0][0].Text);
        Assert.Contains("second", sheets[1].Rows[1][0].Text);
    }

    [Fact]
    public void Parse_AHeaderRepeatedInTheTable_IsKeptOnce()
    {
        // Arrange
        var html = "<table><tr><th>A</th></tr><tr><td>1</td></tr><tr><th>A</th></tr><tr><td>2</td></tr></table>";

        // Act
        var sheet = Assert.Single(ResultsTables.Parse(html));

        // Assert
        Assert.Equal(["A", "1", "2"], sheet.Rows.Select(r => r[0].Text));
    }

    [Theory]
    [InlineData("<span class=\"dp-number\">12345678901234567890</span>", false)]
    [InlineData("<span class=\"dp-number\">1.5</span>", true)]
    [InlineData("<span class=\"dp-number\">50%</span>", false)]
    public void Parse_ANumberTooLongOrNotPlain_StaysText(string cell, bool isNumber)
    {
        // Act
        var sheet = Assert.Single(ResultsTables.Parse($"<table><tr><td>{cell}</td></tr></table>"));

        // Assert
        Assert.Equal(isNumber ? CellKind.Number : CellKind.Text, sheet.Rows[0][0].Kind);
    }

    [Fact]
    public void Parse_NothingToShow_IsNoSheets()
    {
        Assert.Empty(ResultsTables.Parse(""));
        Assert.Empty(ResultsTables.Parse("<style>a { }</style><div> </div>"));
    }
}
