using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Invoklet.Services.Updates;
using Invoklet.Windows;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

public sealed class AboutUpdateCheckTests
{
    [AvaloniaFact]
    public void Opening_UpdateIsAvailable_ShowsTheVersionAndReleaseButton()
    {
        var checker = new Mock<IUpdateCheckService>();
        checker.Setup(c => c.CheckNowAsync(It.IsAny<Version>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResult(true,
                new UpdateAvailable(new Version(0, 11, 7), new Uri("https://github.com/tervforge/invoklet/releases/tag/v0.11.7"))));
        var sut = new AboutWindow(checker.Object);

        sut.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Invoklet 0.11.7 is available.", sut.FindControl<TextBlock>("UpdateStatus")!.Text);
        Assert.True(sut.FindControl<Button>("ViewReleaseButton")!.IsVisible);
        checker.Verify(c => c.CheckNowAsync(It.IsAny<Version>(), It.IsAny<CancellationToken>()), Times.Once);
        sut.Close();
    }

    [AvaloniaFact]
    public void Opening_CurrentVersion_ShowsThatInvokletIsUpToDate()
    {
        var checker = new Mock<IUpdateCheckService>();
        checker.Setup(c => c.CheckNowAsync(It.IsAny<Version>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResult(true, null));
        var sut = new AboutWindow(checker.Object);

        sut.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Invoklet is up to date.", sut.FindControl<TextBlock>("UpdateStatus")!.Text);
        Assert.False(sut.FindControl<Button>("ViewReleaseButton")!.IsVisible);
        sut.Close();
    }
}
