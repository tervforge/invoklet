using Invoklet.UserControls;
using Xunit;

namespace Invoklet.NonUI.Tests.App.UserControls;

// How a label is cut to highlight what the settings search matches: every occurrence, in any case.
[Trait("Platform", "Independent")]
public class SearchHighlightTests
{
    [Fact]
    public void Split_Should_MarkEveryOccurrence_IgnoringCase()
    {
        // Arrange / Act
        var pieces = SearchHighlight.Split("Open Script or open a folder", "OPEN");

        // Assert
        Assert.Equal([("Open", true), (" Script or ", false), ("open", true), (" a folder", false)], pieces);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz")]
    public void Split_WithoutAMatch_Should_KeepTheWholeText(string? query)
    {
        // Arrange / Act
        var pieces = SearchHighlight.Split("Show line numbers", query);

        // Assert
        Assert.Equal([("Show line numbers", false)], pieces);
    }

    [Fact]
    public void Split_Should_IgnoreTheSpacesAroundTheQuery()
    {
        // Arrange / Act
        var pieces = SearchHighlight.Split("Word wrap", " wrap ");

        // Assert
        Assert.Equal([("Word ", false), ("wrap", true)], pieces);
    }
}
