using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Invoklet.Services.Tabs;
using Invoklet.UserControls.Models;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// What a tab header shows at its right end: the close button on the chosen tab, the tab's status on the others.
public sealed class EditorTabsControlStatusTests
{
    private static Panel StatusOf(TabItem header) =>
        header.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("TabStatus"));

    private static Button CloseOf(TabItem header) =>
        header.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("TabClose"));

    private static StackPanel RowOf(TabItem header) => (StackPanel)StatusOf(header).Parent!.Parent!;

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task Status_AnInactiveTabWithUnsavedText_ShowsItInThePlaceOfTheCloseButton()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);

        // Act
        fixture.ViewModel!.Tabs[0].IsDirty = true;
        Settle(fixture.Window);

        // Assert
        var header = fixture.Headers[0];
        Assert.True(StatusOf(header).IsEffectivelyVisible);
        var dot = Assert.IsType<global::Avalonia.Controls.Shapes.Ellipse>(Assert.Single(StatusOf(header).Children, c => c.IsVisible));
        Assert.Equal(7, dot.Width);
        Assert.Equal("Unsaved changes", ToolTip.GetTip(RowOf(header)));
    }

    [AvaloniaFact]
    public async Task Status_AnInactiveIdleTab_ShowsADotAndNoText()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);

        // Act
        Settle(fixture.Window);

        // Assert
        var dot = Assert.IsType<global::Avalonia.Controls.Shapes.Ellipse>(Assert.Single(StatusOf(fixture.Headers[0]).Children, c => c.IsVisible));
        Assert.Equal(7, dot.Width);
        Assert.Null(ToolTip.GetTip(RowOf(fixture.Headers[0])));
    }

    [AvaloniaFact]
    public async Task Status_TheActiveTabWithAFailedRun_ShowsTheCloseButtonAndKeepsTheText()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);

        // Act
        fixture.ViewModel!.ActiveTab!.HadRunError = true;
        Settle(fixture.Window);

        // Assert
        var header = fixture.Headers[1];
        Assert.False(StatusOf(header).IsEffectivelyVisible);
        Assert.True(CloseOf(header).IsEffectivelyVisible);
        Assert.Equal("The last run failed", ToolTip.GetTip(RowOf(header)));
        Assert.Equal("The last run failed", AutomationProperties.GetHelpText(RowOf(header)));
    }

    [AvaloniaFact]
    public async Task Status_ChoosingATabWithAStatus_KeepsTheWidthOfEveryTab()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);
        var tabs = fixture.ViewModel!.Tabs;
        tabs[0].IsDirty = true;
        Settle(fixture.Window);
        var dirtyWidth = fixture.Headers[0].Bounds.Width;
        var otherWidth = fixture.Headers[1].Bounds.Width;

        // Act
        fixture.Tabs.SelectedIndex = 0;
        Settle(fixture.Window);

        // Assert
        Assert.Equal(dirtyWidth, fixture.Headers[0].Bounds.Width, 1);
        Assert.Equal(otherWidth, fixture.Headers[1].Bounds.Width, 1);
        Assert.Equal(0, fixture.Tabs.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Status_HoveringAnInactiveTab_GivesWayToTheCloseButtonWithoutAJump()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);
        fixture.ViewModel!.Tabs[0].IsDirty = true;
        Settle(fixture.Window);
        var header = fixture.Headers[0];
        var width = header.Bounds.Width;

        // Act
        ((IPseudoClasses)header.Classes).Add(":pointerover");
        Settle(fixture.Window);

        // Assert
        Assert.False(StatusOf(header).IsEffectivelyVisible);
        Assert.Equal(width, header.Bounds.Width, 1);
        Assert.Equal("Unsaved changes", ToolTip.GetTip(RowOf(header)));
    }

    [AvaloniaFact]
    public async Task Status_KeyboardFocusOnAnInactiveTab_GivesWayToTheCloseButton()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);
        fixture.ViewModel!.Tabs[0].IsDirty = true;
        Settle(fixture.Window);
        var header = fixture.Headers[0];

        // Act
        CloseOf(header).Focus(NavigationMethod.Tab);
        Settle(fixture.Window);

        // Assert
        Assert.False(StatusOf(header).IsEffectivelyVisible);
    }

    // The application's scroll bar is ten pixels wide; over the headers it must stay the thin line of the strip.
    [AvaloniaFact]
    public async Task StripScrollBar_InTheApplicationsTheme_StaysThin()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();

        // Act
        await fixture.CreateSutAsync(tabs: 20, width: 500, applicationTheme: true);
        Settle(fixture.Window);

        // Assert
        var bar = fixture.Part<ScrollViewer>("PART_TabStripScrollViewer").GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == global::Avalonia.Layout.Orientation.Horizontal);
        Assert.Equal(3, bar.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task Close_AnInactiveTab_StillClosesThatTab()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 2);
        var tabs = fixture.ViewModel!.Tabs;
        var inactive = tabs[0];

        // Act
        CloseOf(fixture.Headers[0]).Command!.Execute(null);
        await fixture.Strip!.TabsReadyForTests();
        Settle(fixture.Window);

        // Assert
        Assert.DoesNotContain(inactive, tabs);
        Assert.Single(tabs);
    }

    [AvaloniaFact]
    public async Task ClearResults_AfterAFailedRun_ForgetsTheFailure()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 1);
        var tab = fixture.ViewModel!.ActiveTab!;
        tab.HadRunError = true;
        ITabCommandTarget target = fixture.Strip!.ControlForTests(tab)!;

        // Act
        target.ClearResults();

        // Assert
        Assert.False(tab.HadRunError);
        Assert.Equal(TabStatus.None, tab.Status);
    }

    [AvaloniaFact]
    public async Task ClearResults_DuringARun_KeepsWhatTheRunReported()
    {
        // Arrange
        await using var fixture = new EditorTabsControlStripFixture();
        await fixture.CreateSutAsync(tabs: 1);
        var tab = fixture.ViewModel!.ActiveTab!;
        tab.HadRunError = true;
        tab.RunState = RunState.Run;
        ITabCommandTarget target = fixture.Strip!.ControlForTests(tab)!;

        // Act
        target.ClearResults();

        // Assert
        Assert.True(tab.HadRunError);
        tab.RunState = RunState.Idle;
    }
}
