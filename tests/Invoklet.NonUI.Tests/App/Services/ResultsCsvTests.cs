using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Platform", "Independent")]
public sealed class ResultsCsvTests
{
    private static string Build(params string[][] rows) => ResultsCsv.Build(new ResultsTable(rows));

    [Fact]
    public void Build_Rows_AreLinesOfCommaSeparatedFieldsEndingWithCrLf()
    {
        Assert.Equal("Name,Age\r\nAnn,31\r\n", Build(["Name", "Age"], ["Ann", "31"]));
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("", "")]
    public void Build_AFieldWithACommaAQuoteOrALineBreak_IsQuoted(string field, string expected)
    {
        Assert.Equal(expected + "\r\n", Build([field]));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("+1 555", "'+1 555")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("-5", "-5")]
    [InlineData("a=b", "a=b")]
    public void Build_AFieldAFormulaCouldStartFrom_IsKeptText(string field, string expected)
    {
        Assert.Equal(expected + "\r\n", Build([field]));
    }
}
