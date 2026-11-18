using Invoklet.NonUI.Tests.Plugins.Fixtures;
using Invoklet.Plugins.Hosting;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Platform", "Independent")]
public sealed class PluginFeedResolverTests
{
    private readonly PluginFeedResolverFixture _fixture = new();

    [Fact]
    public async Task ChooseAsync_NewestNeedsANewerInvoklet_ChoosesTheNewestCompatibleVersion()
    {
        // Arrange
        _fixture.Offer(["2.0.0", "1.1.0"], version => PluginFeedResolverFixture.Theme(version,
            version == "2.0.0" ? ""","minAppVersion":"9.0.0" """ : ""));
        var sut = _fixture.CreateSut();
        var skipped = new List<string>();

        // Act
        var choice = await sut.ChooseAsync(PluginFeedResolverFixture.Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1.1.0", choice?.Version);
        Assert.Equal(["2.0.0: needs Invoklet 9.0.0 or newer."], choice?.Skipped);
    }

    [Theory]
    [InlineData("""{"id":"example.theme.ocean","version":"1.0.0","displayName":"Ocean","roles":{"theme":2},"assets":{"themes":["t.json"]}}""",
        "1.0.0: written for theme contract 2, which this Invoklet does not support.")]
    [InlineData("""{"id":"example.theme.other","version":"1.0.0","displayName":"Ocean","roles":{"theme":1},"assets":{"themes":["t.json"]}}""",
        "1.0.0: the manifest names a different package or version.")]
    [InlineData("""{"id":"example.theme.ocean","version":"1.0.0","displayName":"Ocean","roles":{"language":1},"entry":{"assembly":"L.dll","type":"L.Plugin"}}""",
        "1.0.0: only theme, connector and library packages can be installed.")]
    [InlineData(null, "1.0.0: the package has no valid plugin/invoklet-plugin.json.")]
    [InlineData("{", "1.0.0: the package has no valid plugin/invoklet-plugin.json.")]
    public async Task ChooseAsync_NoCompatibleVersion_ReturnsNullWithTheReason(string? manifest, string reason)
    {
        // Arrange
        _fixture.Offer(["1.0.0"], _ => manifest);
        var sut = _fixture.CreateSut();
        var skipped = new List<string>();

        // Act
        var choice = await sut.ChooseAsync(PluginFeedResolverFixture.Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(choice);
        Assert.Equal([reason], skipped);
    }

    [Fact]
    public async Task ChooseAsync_ABundledId_IsRefusedWithoutAskingTheFeed()
    {
        // Arrange
        var sut = _fixture.CreateSut(new BundledPluginCatalog([new BundledPluginEntry(PluginFeedResolverFixture.Id, "1.0.0", new string('a', 64))]));
        var skipped = new List<string>();

        // Act
        var choice = await sut.ChooseAsync(PluginFeedResolverFixture.Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(choice);
        Assert.Equal(["'example.theme.ocean' is included with Invoklet and is updated with it."], skipped);
        _fixture.Feed.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChooseAsync_ManyIncompatibleVersions_DownloadsAtMostTheCandidateLimit()
    {
        // Arrange
        var versions = Enumerable.Range(1, PluginFeedResolver.MaxCandidates + 3).Select(minor => $"1.{minor}.0").Reverse().ToArray();
        _fixture.Offer(versions, version => PluginFeedResolverFixture.Theme(version, ""","minAppVersion":"9.0.0" """));
        var sut = _fixture.CreateSut();
        var skipped = new List<string>();

        // Act
        var choice = await sut.ChooseAsync(PluginFeedResolverFixture.Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(choice);
        Assert.Equal(PluginFeedResolver.MaxCandidates, skipped.Count);
        _fixture.Feed.Verify(feed => feed.DownloadAsync(PluginFeedResolverFixture.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(PluginFeedResolver.MaxCandidates));
    }

    [Fact]
    public async Task ChooseAsync_TheFeedHasNoVersions_SaysSo()
    {
        // Arrange
        _fixture.Offer([], _ => null);
        var sut = _fixture.CreateSut();
        var skipped = new List<string>();

        // Act
        var choice = await sut.ChooseAsync(PluginFeedResolverFixture.Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(choice);
        Assert.Equal(["The feed has no versions of 'example.theme.ocean'."], skipped);
    }
}
