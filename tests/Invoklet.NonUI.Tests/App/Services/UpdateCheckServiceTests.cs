using Invoklet.NonUI.Tests.App.Services.Fixtures;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Platform", "Independent")]
public sealed class UpdateCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_FirstCheckFindsNewerRelease_ReturnsItsVersionAndPage()
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.LatestTag("v0.11.7");
        var sut = fixture.CreateSut();

        var result = await sut.CheckDailyAsync(new Version(0, 11, 6), Now, TestContext.Current.CancellationToken);

        Assert.True(result.Checked);
        Assert.NotNull(result.Available);
        Assert.Equal(new Version(0, 11, 7), result.Available.Version);
        Assert.Equal("https://github.com/tervforge/invoklet/releases/tag/v0.11.7", result.Available.ReleaseUri.AbsoluteUri);
        Assert.Equal(Now, fixture.Session.LastUpdateCheckUtc);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_PreviousCheckWasLessThanADayAgo_DoesNotRequestGitHub()
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.Session.LastUpdateCheckUtc = Now.AddHours(-23);
        var sut = fixture.CreateSut();

        var result = await sut.CheckDailyAsync(new Version(0, 11, 6), Now, TestContext.Current.CancellationToken);

        Assert.False(result.Checked);
        Assert.Null(result.Available);
        fixture.Metadata.VerifyNoOtherCalls();
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Theory]
    [InlineData("v0.11.6")]
    [InlineData("v0.11.5")]
    public async Task CheckAsync_ReleaseIsNotNewer_ReturnsNothing(string tag)
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.LatestTag(tag);
        var sut = fixture.CreateSut();

        var result = await sut.CheckDailyAsync(new Version(0, 11, 6), Now, TestContext.Current.CancellationToken);

        Assert.True(result.Checked);
        Assert.Null(result.Available);
    }

    [Fact]
    public async Task CheckAsync_ReleaseMetadataIsMalformed_ReturnsUnchecked()
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.LatestTag("not-a-version");
        var sut = fixture.CreateSut();

        var result = await sut.CheckDailyAsync(new Version(0, 11, 6), Now, TestContext.Current.CancellationToken);

        Assert.False(result.Checked);
        Assert.Null(result.Available);
    }

    [Fact]
    public async Task CheckAsync_GitHubIsUnavailable_ReturnsNothingAndKeepsTheDailyInterval()
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.LatestTag(null);
        var sut = fixture.CreateSut();

        var result = await sut.CheckDailyAsync(new Version(0, 11, 6), Now, TestContext.Current.CancellationToken);

        Assert.False(result.Checked);
        Assert.Null(result.Available);
        Assert.Equal(Now, fixture.Session.LastUpdateCheckUtc);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    [Fact]
    public async Task CheckNowAsync_PreviousDailyCheckWasRecent_StillRequestsGitHub()
    {
        var fixture = new UpdateCheckServiceFixture();
        fixture.Session.LastUpdateCheckUtc = Now;
        fixture.LatestTag("v0.11.7");
        var sut = fixture.CreateSut();

        var result = await sut.CheckNowAsync(new Version(0, 11, 6), TestContext.Current.CancellationToken);

        Assert.True(result.Checked);
        Assert.NotNull(result.Available);
        fixture.Metadata.Verify(m => m.ReadStringAsync(It.IsAny<Uri>(), "tag_name", It.IsAny<CancellationToken>()), Times.Once);
    }
}
