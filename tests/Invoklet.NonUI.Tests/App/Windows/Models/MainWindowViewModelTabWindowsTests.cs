using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// A tab moved to a window of its own stays the workspace's: a new tab still opens in the main window, and closing one of the main window's
// tabs hands the focus to another of them, not to the moved tab.
[Trait("Platform", "Independent")]
public sealed class MainWindowViewModelTabWindowsTests
{
    [Fact]
    public void NewMdRunFile_WhileATabOfItsOwnWindowIsActive_OpensInTheMainWindow()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        sut.Tabs[1].WindowId = 7;
        sut.ActiveTab = sut.Tabs[1];

        // Act
        sut.NewMdRunFile();

        // Assert
        Assert.Equal(0, sut.Tabs[^1].WindowId);
    }

    [Fact]
    public void CloseTab_TheActiveTabOfTheMainWindow_ActivatesAnotherOfItsTabsNotAMovedOne()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        var (first, second, third) = (sut.Tabs[0], sut.Tabs[1], sut.Tabs[2]);
        third.WindowId = 7;
        sut.ActiveTab = second;

        // Act
        sut.CloseTab(second);

        // Assert
        Assert.Same(first, sut.ActiveTab);
        Assert.Equal([first, third], sut.Tabs);
    }

    [Fact]
    public void CloseTab_TheLastTabOfAWindow_ActivatesATabOfAnotherWindow()
    {
        // Arrange
        using var fixture = new MainWindowViewModelMoveTabFixture();
        var sut = fixture.CreateSut();
        var moved = sut.Tabs[1];
        moved.WindowId = 7;
        sut.ActiveTab = moved;

        // Act
        sut.CloseTab(moved);

        // Assert
        Assert.Same(sut.Tabs[^1], sut.ActiveTab);
        Assert.Equal(0, sut.ActiveTab!.WindowId);
    }
}
