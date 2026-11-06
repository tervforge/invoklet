using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// A tab dragged along the strip takes its new place among the tabs.
[Trait("Platform", "Independent")]
public sealed class MainWindowViewModelMoveTabTests
{
    [Fact]
    public void MoveTab_TheLastTabToTheFront_PutsItFirstAndKeepsTheOthersInOrder()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        var (first, second, third) = (sut.Tabs[0], sut.Tabs[1], sut.Tabs[2]);

        // Act
        sut.MoveTab(third, 0);

        // Assert
        Assert.Equal([third, first, second], sut.Tabs);
    }

    [Fact]
    public void MoveTab_AnIndexPastTheTabs_LeavesTheOrderAlone()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        var before = sut.Tabs.ToList();

        // Act
        sut.MoveTab(before[0], 3);

        // Assert
        Assert.Equal(before, sut.Tabs);
    }

    [Fact]
    public void MoveTab_TheActiveTab_StaysActive()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        var active = sut.Tabs[2];
        sut.ActiveTab = active;

        // Act
        sut.MoveTab(active, 0);

        // Assert
        Assert.Same(active, sut.ActiveTab);
    }
}
