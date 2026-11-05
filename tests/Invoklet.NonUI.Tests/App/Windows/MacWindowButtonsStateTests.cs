using Avalonia.Controls;
using Invoklet.Windows;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows;

// The green button and a double click on the title bar do what they do on every Mac window.
[Trait("Platform", "Independent")]
public sealed class MacWindowButtonsStateTests
{
    // The green button enters full screen and leaves it; with Option it zooms and unzooms. It used to toggle a zoom only, so a window put
    // in full screen from the menu was zoomed by it instead of coming back.
    [Theory]
    [InlineData(WindowState.Normal, false, WindowState.FullScreen)]
    [InlineData(WindowState.Maximized, false, WindowState.FullScreen)]
    [InlineData(WindowState.FullScreen, false, WindowState.Normal)]
    [InlineData(WindowState.FullScreen, true, WindowState.Normal)]
    [InlineData(WindowState.Normal, true, WindowState.Maximized)]
    [InlineData(WindowState.Maximized, true, WindowState.Normal)]
    public void TheGreenButton_Should_EnterAndLeaveFullScreen_AndZoomWithOption(WindowState current, bool option, WindowState expected)
        => Assert.Equal(expected, MacWindowButtons.GreenButtonState(current, option));

    [Theory]
    [InlineData(WindowState.Normal, WindowState.Maximized)]
    [InlineData(WindowState.Maximized, WindowState.Normal)]
    [InlineData(WindowState.FullScreen, WindowState.FullScreen)]
    public void ADoubleClickOnTheTitleBar_Should_Zoom_AndLeaveFullScreenAlone(WindowState current, WindowState expected)
        => Assert.Equal(expected, MacWindowButtons.TitleDoubleClickState(current));
}
