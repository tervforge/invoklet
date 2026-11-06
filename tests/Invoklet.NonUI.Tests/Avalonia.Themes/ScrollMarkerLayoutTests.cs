using Invoklet.Avalonia.Themes.Scrolling;
using Xunit;

namespace Invoklet.NonUI.Tests.Avalonia.Themes;

[Trait("Platform", "Independent")]
public sealed class ScrollMarkerLayoutTests
{
    // Lines twenty pixels tall, one under the other.
    private static ScrollMarkerGeometry Lines(int count) => new(count, count * 20, 20, line => (line - 1) * 20);

    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 27)]
    [InlineData(7, 127)]
    public void Rows_ContentThatFits_CentresAMarkOnItsLine(int line, double expectedTop)
    {
        // Arrange
        ScrollMarker[] markers = [new(line, ScrollMarkerKind.Error)];

        // Act
        var row = Assert.Single(ScrollMarkerLayout.Rows(markers, Lines(7), height: 500));

        // Assert
        Assert.Equal(expectedTop, row.Top);
        Assert.Equal(ScrollMarkerLayout.RowHeight, row.Height);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(501, 247.25)]
    [InlineData(1000, 494)]
    public void Rows_LongContent_ScalesItToTheHeight(int line, double expectedTop)
    {
        // Arrange
        ScrollMarker[] markers = [new(line, ScrollMarkerKind.Error)];

        // Act
        var row = Assert.Single(ScrollMarkerLayout.Rows(markers, Lines(1000), height: 500));

        // Assert
        Assert.Equal(expectedTop, row.Top, 3);
        Assert.Equal(ScrollMarkerLayout.RowHeight, row.Height);
    }

    // Folded or wrapped lines: the place comes from the content, not from the number of the line.
    [Fact]
    public void Rows_LinesAtTheirOwnPlaces_FollowsThem()
    {
        // Arrange
        ScrollMarker[] markers = [new(90, ScrollMarkerKind.Error)];
        var folded = new ScrollMarkerGeometry(100, 200, 20, _ => 60);

        // Act
        var row = Assert.Single(ScrollMarkerLayout.Rows(markers, folded, height: 500));

        // Assert
        Assert.Equal(67, row.Top);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(10, 0)]
    [InlineData(10, 2)]
    public void Rows_NoContentOrNoRoom_HasNoRows(int lineCount, double height)
    {
        // Arrange
        ScrollMarker[] markers = [new(1, ScrollMarkerKind.Error)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(lineCount), height);

        // Assert
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(11)]
    public void Rows_ALineOutsideTheContent_IsLeftOut(int line)
    {
        // Arrange
        ScrollMarker[] markers = [new(line, ScrollMarkerKind.Error)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(10), height: 500);

        // Assert
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData(ScrollMarkerKind.Execution, ScrollMarkerKind.Error)]
    [InlineData(ScrollMarkerKind.Error, ScrollMarkerKind.Warning)]
    [InlineData(ScrollMarkerKind.Warning, ScrollMarkerKind.Info)]
    [InlineData(ScrollMarkerKind.Info, ScrollMarkerKind.Breakpoint)]
    public void Rows_MarkersOnOneRow_ShowTheMoreImportant(ScrollMarkerKind stronger, ScrollMarkerKind weaker)
    {
        // Arrange: a thousand lines over a hundred pixels, so neighbouring lines share a row
        ScrollMarker[] markers = [new(500, weaker), new(501, stronger)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(1000), height: 100);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal(stronger, row.Kind);
        Assert.Equal(501, row.Line);
        Assert.Equal(2, row.Markers.Count);
    }

    [Fact]
    public void Rows_OneKindOnNeighbouringLines_FlowIntoOneMark()
    {
        // Arrange
        ScrollMarker[] markers = [new(2, ScrollMarkerKind.Error), new(3, ScrollMarkerKind.Error), new(4, ScrollMarkerKind.Error)];

        // Act
        var row = Assert.Single(ScrollMarkerLayout.Rows(markers, Lines(7), height: 500));

        // Assert
        Assert.Equal(2, row.Line);
        Assert.Equal(27, row.Top);
        Assert.Equal(46, row.Height);
        Assert.Equal(3, row.Markers.Count);
    }

    [Fact]
    public void Rows_ALineBetween_KeepsTheMarksApart()
    {
        // Arrange
        ScrollMarker[] markers = [new(2, ScrollMarkerKind.Error), new(4, ScrollMarkerKind.Error)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(7), height: 500);

        // Assert
        Assert.Equal([2, 4], rows.Select(r => r.Line));
        Assert.All(rows, r => Assert.Equal(ScrollMarkerLayout.RowHeight, r.Height));
    }

    [Fact]
    public void Rows_AnotherKindOnTheNextLine_StaysApart()
    {
        // Arrange
        ScrollMarker[] markers = [new(2, ScrollMarkerKind.Error), new(3, ScrollMarkerKind.Warning)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(7), height: 500);

        // Assert
        Assert.Equal([ScrollMarkerKind.Error, ScrollMarkerKind.Warning], rows.Select(r => r.Kind));
    }

    [Fact]
    public void Rows_BreakpointsOnNeighbouringLines_StayApart()
    {
        // Arrange
        ScrollMarker[] markers = [new(2, ScrollMarkerKind.Breakpoint), new(3, ScrollMarkerKind.Breakpoint)];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(7), height: 500);

        // Assert
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Rows_TheSameMarkerTwice_CountsOnce()
    {
        // Arrange
        ScrollMarker[] markers = [new(5, ScrollMarkerKind.Info, "x"), new(5, ScrollMarkerKind.Info, "x")];

        // Act
        var rows = ScrollMarkerLayout.Rows(markers, Lines(10), height: 500);

        // Assert
        Assert.Single(Assert.Single(rows).Markers);
    }

    [Fact]
    public void Rows_TheSameMarkersInAnotherOrder_GiveTheSameRows()
    {
        // Arrange
        ScrollMarker a = new(5, ScrollMarkerKind.Warning, "a");
        ScrollMarker b = new(5, ScrollMarkerKind.Warning, "b");

        // Act
        var one = ScrollMarkerLayout.Rows([a, b], Lines(10), height: 500);
        var other = ScrollMarkerLayout.Rows([b, a], Lines(10), height: 500);

        // Assert
        Assert.Equal(one[0].Markers, other[0].Markers);
        Assert.Equal("Line 5: Warning: a (and 1 more)", ScrollMarkerLayout.Describe(other[0]));
    }

    [Theory]
    [InlineData(ScrollMarkerKind.Error, "boom", "Line 3: Error: boom")]
    [InlineData(ScrollMarkerKind.Warning, "careful", "Line 3: Warning: careful")]
    [InlineData(ScrollMarkerKind.Info, "note", "Line 3: Information: note")]
    [InlineData(ScrollMarkerKind.Breakpoint, null, "Line 3: Breakpoint")]
    [InlineData(ScrollMarkerKind.Execution, null, "Line 3: Current line")]
    public void Describe_AMark_NamesItsLineAndKind(ScrollMarkerKind kind, string? message, string expected)
    {
        // Arrange
        var row = ScrollMarkerLayout.Rows([new ScrollMarker(3, kind, message)], Lines(10), height: 500)[0];

        // Act
        var text = ScrollMarkerLayout.Describe(row);

        // Assert
        Assert.Equal(expected, text);
    }
}
