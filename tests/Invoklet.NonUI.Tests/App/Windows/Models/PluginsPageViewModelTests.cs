using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Windows.Models;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

public sealed class PluginsPageViewModelTests
{
    private const string ReinstallHint = "Remove the plugin and install its package again to approve the new contents.";

    [Fact]
    public void Refresh_ChangedContentsOfAPreferencesPackage_ShowsContentChangedWithTheReinstallHint()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(PluginTrust.Trusted, PluginPackageInstaller.InstalledSource);

        // Act
        sut.Refresh();

        // Assert
        var row = Assert.Single(sut.Rows, item => item.Status == "Content changed");
        Assert.True(row.CanRemove);
        Assert.Contains(ReinstallHint, row.Diagnostics);
    }

    [Theory]
    [InlineData(PluginTrust.Trusted, "manual")]
    [InlineData(PluginTrust.Bundled, null)]
    public void Refresh_ChangedContentsThatCannotBeRemovedHere_ShowsContentChangedWithoutTheReinstallHint(PluginTrust trust, string? source)
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(trust, source);

        // Act
        sut.Refresh();

        // Assert
        var row = Assert.Single(sut.Rows, item => item.Status == "Content changed");
        Assert.False(row.CanRemove);
        Assert.DoesNotContain(ReinstallHint, row.Diagnostics);
    }

    [Fact]
    public async Task RemoveAsync_DeclinedConfirmation_ChangesNothing()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(PluginTrust.Trusted, PluginPackageInstaller.InstalledSource);
        sut.Refresh();

        // Act
        await sut.RemoveAsync(sut.Rows.Single(row => row.CanRemove), () => Task.FromResult(false));

        // Assert
        fixture.CreateInstaller.Verify(create => create(), Times.Never);
        Assert.Empty(sut.Diagnostics);
    }

    [Fact]
    public async Task BrowseAsync_TheFeedRefusesAccess_ShowsTheFeedMessageAndNoPackages()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        fixture.Feed.Setup(feed => feed.ListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PluginFeedException(PluginFeedError.Unauthorized));
        var sut = fixture.CreateFeedSut();

        // Act
        await sut.BrowseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The plugin feed refused access. It needs credentials, or the ones configured are not accepted.", sut.FeedStatus);
        Assert.Empty(sut.FeedPackages);
        Assert.False(sut.IsFeedBusy);
    }

    [Fact]
    public async Task BrowseAsync_ListedPackages_OfferOnlyThoseNeitherBundledNorInstalled()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        fixture.Feed.Setup(feed => feed.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            PluginsPageViewModelFixture.Package("example.theme.bundled"),
            PluginsPageViewModelFixture.Package("example.theme.installed", "1.1.0"),
            PluginsPageViewModelFixture.Package("example.theme.new")]);
        var sut = fixture.CreateFeedSut();

        // Act
        await sut.BrowseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(sut.FeedStatus);
        Assert.Equal(
            [("example.theme.bundled", "Included with Invoklet", false),
             ("example.theme.installed", "Installed 1.0.0; 1.1.0 is available. To update, remove it and install again.", false),
             ("example.theme.new", "", true)],
            sut.FeedPackages.Select(row => (row.Package.Id, row.State, row.CanInstall)));
    }

    [Fact]
    public async Task BrowseAsync_PackagesWithAPublisherPrefix_MatchTheirBundledAndInstalledPlugins()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        fixture.Feed.Setup(feed => feed.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            PluginsPageViewModelFixture.Package("tervforge.example.theme.bundled"),
            PluginsPageViewModelFixture.Package("tervforge.example.theme.installed")]);
        var sut = fixture.CreateFeedSut();

        // Act
        await sut.BrowseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("tervforge.example.theme.bundled", "Included with Invoklet", false),
             ("tervforge.example.theme.installed", "Installed 1.0.0", false)],
            sut.FeedPackages.Select(row => (row.Package.Id, row.State, row.CanInstall)));
    }

    [Fact]
    public async Task InstallFromFeedAsync_NoCompatibleVersion_ShowsTheReasonWithoutAskingForApproval()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        fixture.Feed.Setup(feed => feed.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([PluginsPageViewModelFixture.Package("example.theme.new")]);
        fixture.Feed.Setup(feed => feed.GetVersionsAsync("example.theme.new", It.IsAny<CancellationToken>())).ReturnsAsync(["1.0.0"]);
        fixture.Feed.Setup(feed => feed.DownloadAsync("example.theme.new", "1.0.0", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        var sut = fixture.CreateFeedSut();
        await sut.BrowseAsync(TestContext.Current.CancellationToken);
        var confirm = new Mock<Func<PluginManifest, string, Task<bool>>>();

        // Act
        await sut.InstallFromFeedAsync(sut.FeedPackages.Single(), confirm.Object, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("No version of example.theme.new works with this Invoklet. Newest: 1.0.0: the package has no valid plugin/invoklet-plugin.json.",
            sut.FeedStatus);
        confirm.Verify(ask => ask(It.IsAny<PluginManifest>(), It.IsAny<string>()), Times.Never);
        fixture.CreateInstaller.Verify(create => create(), Times.Never);
    }
}
