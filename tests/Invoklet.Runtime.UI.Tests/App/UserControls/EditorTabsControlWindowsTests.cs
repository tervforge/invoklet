using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// A tab moved out of the main window's strip goes to a window of its own, which shows only its body, and comes back, with its editor,
// when that window closes; the workspace keeps every tab.
public class EditorTabsControlWindowsTests
{
    [AvaloniaFact]
    public async Task MoveToNewWindow_ATab_ShowsItInAWindowOfItsOwnWithTheSameEditorAndNoHeaders()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 3);
        var tab = fixture.ViewModel!.Tabs[1];
        var editor = sut.ControlForTests(tab);

        // Act
        var other = sut.MoveToNewWindow(tab)!;
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal([tab], other.OwnTabs);
        Assert.Same(editor, other.ControlForTests(tab));
        Assert.Equal(2, fixture.Headers.Count);
        Assert.Equal(3, fixture.ViewModel.Tabs.Count);
        Assert.Same(tab, fixture.ViewModel.ActiveTab);
        Assert.True(EditorTabsControlStripFixture.WindowOf(other)!.IsVisible);
        Assert.False(EditorTabsControlStripFixture.StripRowOf(other).IsVisible);
        Assert.True(EditorTabsControlStripFixture.StripRowOf(sut).IsVisible);
    }

    [AvaloniaFact]
    public async Task Close_TheWindowOfATab_GivesTheTabBackToTheMainWindowWithItsEditor()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 3);
        var tab = fixture.ViewModel!.Tabs[1];
        var editor = sut.ControlForTests(tab);
        var other = sut.MoveToNewWindow(tab)!;
        var window = EditorTabsControlStripFixture.WindowOf(other)!;
        var closed = false;
        window.Closed += (_, _) => closed = true;

        // Act
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(3, fixture.Headers.Count);
        Assert.Equal(3, fixture.ViewModel.Tabs.Count);
        Assert.Equal(0, tab.WindowId);
        Assert.Same(editor, sut.ControlForTests(tab));
        Assert.True(closed);
    }

    // The tab closed for good (its file deleted, say) leaves its window with nothing to show.
    [AvaloniaFact]
    public async Task CloseTab_TheTabOfItsOwnWindow_ClosesTheWindow()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 3);
        var tab = fixture.ViewModel!.Tabs[1];
        var other = sut.MoveToNewWindow(tab)!;
        var window = EditorTabsControlStripFixture.WindowOf(other)!;
        var closed = false;
        window.Closed += (_, _) => closed = true;

        // Act
        fixture.ViewModel.CloseTab(tab);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.True(closed);
        Assert.Equal(2, fixture.ViewModel.Tabs.Count);
    }

    // Closing the main window asks about every tab: the other windows' tabs come back to it first.
    [AvaloniaFact]
    public async Task GatherOtherWindows_TabsInOtherWindows_ComeBackAndTheirWindowsClose()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 3);
        var tab = fixture.ViewModel!.Tabs[2];
        var editor = sut.ControlForTests(tab);
        var other = sut.MoveToNewWindow(tab)!;
        var window = EditorTabsControlStripFixture.WindowOf(other)!;
        var closed = false;
        window.Closed += (_, _) => closed = true;

        // Act
        sut.GatherOtherWindows();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.True(closed);
        Assert.Equal(3, fixture.Headers.Count);
        Assert.Equal(0, tab.WindowId);
        Assert.Same(editor, sut.ControlForTests(tab));
    }

    [AvaloniaFact]
    public async Task NewMdRunFile_WhileATabsWindowIsInUse_OpensTheTabInTheMainWindow()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 2);
        var other = sut.MoveToNewWindow(fixture.ViewModel!.Tabs[1])!;

        // Act
        fixture.ViewModel.NewMdRunFile();
        await sut.TabsReadyForTests(); // the new tab is the main window's
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Single(other.OwnTabs);
        Assert.Equal(2, fixture.Headers.Count);
    }

    // Only the main window's tabs are moved out: a tab's window is that tab's already.
    [AvaloniaFact]
    public async Task MoveToNewWindow_TheTabOfATabsWindow_DoesNothing()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        var sut = await fixture.CreateSutAsync(tabs: 2);
        var tab = fixture.ViewModel!.Tabs[1];
        var other = sut.MoveToNewWindow(tab)!;

        // Act
        var moved = other.MoveToNewWindow(tab);

        // Assert
        Assert.Null(moved);
        Assert.Equal([tab], other.OwnTabs);
    }

    [AvaloniaFact]
    public async Task Drop_FarBelowTheStrip_MovesTheTabToAWindowOfItsOwn()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var tab = fixture.ViewModel!.Tabs[0];
        var from = fixture.InWindow(fixture.Headers[0]).Center;
        var below = new Point(from.X + 20, from.Y + 200);

        // Act
        fixture.Window.MouseDown(from, MouseButton.Left);
        fixture.Window.MouseMove(new Point(from.X + 10, from.Y + 10));
        fixture.Window.MouseMove(below);
        fixture.Window.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.NotEqual(0, tab.WindowId);
        Assert.Equal(2, fixture.Headers.Count);
        Assert.DoesNotContain(fixture.Overlay.Children, c => !c.IsHitTestVisible);
    }
}
