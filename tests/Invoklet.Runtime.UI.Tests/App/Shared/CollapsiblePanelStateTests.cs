using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Invoklet.Shared;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Shared;

// Extracted from MainWindowViewModel.SidebarColumnWidth/ToggleSidebar and
// EditorTabItemViewModel.OutputPanelHeight/ToggleOutputPanel, which duplicated this exact
// collapse/expand-plus-throttled-persist scheme near-verbatim. Neither original call site had a
// direct test for the throttled-persist half (MainWindowViewModelTests only ever checked the
// synchronous "Toggle collapses to zero" behavior) — covered here, once, on the class itself.
public class CollapsiblePanelStateTests
{
    [Fact]
    public void Constructor_Should_StartCollapsed_WhenToldTo()
    {
        var state = new CollapsiblePanelState(collapsed: true, savedSize: 300, defaultExpandedSize: 220, onChanged: (_, _) => { });

        Assert.Equal(0, state.Size.Value);
    }

    [Fact]
    public void Constructor_Should_StartExpanded_AtTheSavedSize_WhenNotCollapsed()
    {
        var state = new CollapsiblePanelState(collapsed: false, savedSize: 300, defaultExpandedSize: 220, onChanged: (_, _) => { });

        Assert.Equal(300, state.Size.Value);
    }

    [Fact]
    public void Toggle_Should_CollapseToZero_WhenCurrentlyExpanded()
    {
        var state = new CollapsiblePanelState(collapsed: false, savedSize: 300, defaultExpandedSize: 220, onChanged: (_, _) => { });

        state.ToggleCommand.Execute().Subscribe();

        Assert.Equal(0, state.Size.Value);
    }

    [Fact]
    public void Toggle_Should_RestoreTheLastExpandedSize_WhenCurrentlyCollapsed()
    {
        var state = new CollapsiblePanelState(collapsed: false, savedSize: 300, defaultExpandedSize: 220, onChanged: (_, _) => { });
        state.ToggleCommand.Execute().Subscribe(); // collapse

        state.ToggleCommand.Execute().Subscribe(); // expand again

        Assert.Equal(300, state.Size.Value);
    }

    [Fact]
    public void Toggle_Should_UseTheDefaultExpandedSize_WhenNeverExpandedBefore()
    {
        var state = new CollapsiblePanelState(collapsed: true, savedSize: 0, defaultExpandedSize: 220, onChanged: (_, _) => { });

        state.ToggleCommand.Execute().Subscribe();

        Assert.Equal(220, state.Size.Value);
    }

    [AvaloniaFact]
    public void ChangingSize_Should_InvokeOnChanged_AfterTheThrottle_WithTheNewCollapsedFlagAndSize()
    {
        bool? collapsed = null;
        double? size = null;
        var state = new CollapsiblePanelState(collapsed: false, savedSize: 300, defaultExpandedSize: 220,
            onChanged: (c, s) => { collapsed = c; size = s; }, saveThrottle: TimeSpan.Zero);

        state.Size = new GridLength(450);
        Dispatcher.UIThread.RunJobs();

        Assert.False(collapsed);
        Assert.Equal(450, size);
    }

    [AvaloniaFact]
    public void ChangingSize_Should_NotInvokeOnChanged_ForTheInitialConstructorValue()
    {
        var invoked = false;
        _ = new CollapsiblePanelState(collapsed: false, savedSize: 300, defaultExpandedSize: 220,
            onChanged: (_, _) => invoked = true, saveThrottle: TimeSpan.Zero);

        Dispatcher.UIThread.RunJobs();

        Assert.False(invoked);
    }
}
