using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// The tab strip: "+" right after the last tab, the list of all tabs at the right edge where no tab can slide under it, and a tab dragged
// along the strip taking its new place.
public sealed class EditorTabsControlStripTests
{
    [AvaloniaFact]
    public async Task Layout_AFewTabs_PutsTheAddButtonRightAfterTheLastTab()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);

        // Act
        var last = fixture.InWindow(fixture.Headers[^1]);
        var add = fixture.InWindow(fixture.Part<Button>("PART_AddTab"));

        // Assert
        Assert.Equal(last.Right, add.Left, 1);
    }

    // More tabs than fit: the strip scrolls, and ends where the list of tabs begins.
    [AvaloniaFact]
    public async Task Layout_MoreTabsThanFit_KeepsTheTabsClearOfTheListButton()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 20, width: 500);

        // Act
        var strip = fixture.InWindow(fixture.Part<ScrollViewer>("PART_TabStripScrollViewer"));
        var list = fixture.InWindow(fixture.Part<Button>("PART_AllTabs"));

        // Assert
        Assert.True(strip.Right <= list.Left, $"the strip ends at {strip.Right}, the list starts at {list.Left}");
        Assert.Equal(fixture.Window.Bounds.Width, list.Right, 1);
    }

    // The strip has no scroll bar: a tab chosen past its visible part is scrolled into view.
    [AvaloniaFact]
    public async Task ActiveTab_ATabPastTheVisiblePart_IsScrolledIntoView()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 20, width: 500);
        fixture.ViewModel!.ActiveTab = fixture.ViewModel.Tabs[0];
        Dispatcher.UIThread.RunJobs();

        // Act
        fixture.ViewModel.ActiveTab = fixture.ViewModel.Tabs[^1];
        Dispatcher.UIThread.RunJobs();
        fixture.Window.UpdateLayout();

        // Assert
        var strip = fixture.InWindow(fixture.Part<ScrollViewer>("PART_TabStripScrollViewer"));
        var last = fixture.InWindow(fixture.Headers[^1]);
        Assert.True(last.Left >= strip.Left && last.Right <= strip.Right + 1, $"the tab lies at {last}, the strip at {strip}");
    }

    [AvaloniaFact]
    public async Task Drag_TheLastTabOntoTheFirst_MovesItToTheFront()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var vm = fixture.ViewModel!;
        var (first, second, third) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2]);
        var from = fixture.InWindow(fixture.Headers[2]).Center;
        var to = new Point(fixture.InWindow(fixture.Headers[0]).Left + 4, from.Y);

        // Act
        fixture.Window.MouseDown(from, MouseButton.Left);
        fixture.Window.MouseMove(new Point(from.X - 10, from.Y));
        fixture.Window.MouseMove(to);
        fixture.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal([third, first, second], vm.Tabs);
        Assert.Same(third, vm.ActiveTab);
    }

    // While carried, the tab's picture follows the pointer and a mark shows where it will go; nothing moves before it is dropped.
    [AvaloniaFact]
    public async Task Drag_WhileCarried_ShowsThePictureAndTheMarkAndMovesNothing()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var vm = fixture.ViewModel!;
        var before = vm.Tabs.ToList();
        var from = fixture.InWindow(fixture.Headers[2]).Center;
        var to = new Point(fixture.InWindow(fixture.Headers[0]).Left + 4, from.Y);

        // Act
        fixture.Window.MouseDown(from, MouseButton.Left);
        fixture.Window.MouseMove(new Point(from.X - 10, from.Y));
        fixture.Window.MouseMove(to);

        // Assert
        var overlay = fixture.Overlay;
        Assert.Equal(2, overlay.Children.Count(c => !c.IsHitTestVisible && c.IsVisible));
        Assert.Equal(before, vm.Tabs);
        fixture.Window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task Drop_AfterACarry_TakesThePictureAndTheMarkAway()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var from = fixture.InWindow(fixture.Headers[2]).Center;
        var to = new Point(fixture.InWindow(fixture.Headers[0]).Left + 4, from.Y);
        fixture.Window.MouseDown(from, MouseButton.Left);
        fixture.Window.MouseMove(new Point(from.X - 10, from.Y));
        fixture.Window.MouseMove(to);

        // Act
        fixture.Window.MouseUp(to, MouseButton.Left);

        // Assert
        Assert.DoesNotContain(fixture.Overlay.Children, c => !c.IsHitTestVisible);
    }

    // The tab can be carried a little below the strip; dropped there, it goes where the mark showed.
    [AvaloniaFact]
    public async Task Drop_JustBelowTheStrip_PutsTheTabWhereTheMarkShowed()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var vm = fixture.ViewModel!;
        var (first, second, third) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2]);
        var from = fixture.InWindow(fixture.Headers[0]).Center;
        var below = new Point(fixture.InWindow(fixture.Headers[2]).Right - 4, from.Y + 30);

        // Act
        fixture.Window.MouseDown(from, MouseButton.Left);
        fixture.Window.MouseMove(new Point(from.X + 10, from.Y));
        fixture.Window.MouseMove(below);
        fixture.Window.MouseUp(below, MouseButton.Left);

        // Assert
        Assert.Equal([second, third, first], vm.Tabs);
    }

    // The headers follow the view model's order, each still with its own script.
    [AvaloniaFact]
    public async Task MoveTab_ATab_MovesItsHeaderAndKeepsItsControl()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 3);
        var vm = fixture.ViewModel!;
        var header = fixture.Headers[2];
        var content = header.Content;

        // Act
        vm.MoveTab(vm.Tabs[2], 0);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Same(header, fixture.Headers[0]);
        Assert.Same(content, fixture.Headers[0].Content);
    }
}
