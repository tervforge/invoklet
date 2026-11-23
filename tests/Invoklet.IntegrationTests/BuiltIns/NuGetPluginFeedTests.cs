using Invoklet.BuiltIns;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.IntegrationTests;

// The NuGet v3 protocol over real HTTP, against a feed served on the loopback address.
[Trait("Category", "Integration")]
public sealed class NuGetPluginFeedTests : IDisposable
{
    private const string Id = "example.theme.ocean";
    private readonly StaticPluginFeed _feed = new();

    public void Dispose() => _feed.Dispose();

    private static string Manifest(string version, string extra = "") =>
        $$"""{"id":"{{Id}}","version":"{{version}}","displayName":"Ocean","roles":{"theme":1},"assets":{"themes":["theme.json"]}{{extra}}}""";

    [Fact]
    public async Task ChooseAsync_TwoVersions_ListsThePluginAndDownloadsTheNewestCompatibleOne()
    {
        // Arrange
        _feed.AddTheme(Id, "1.0.0", Manifest("1.0.0"));
        _feed.AddTheme(Id, "2.0.0", Manifest("2.0.0", ""","minAppVersion":"99.0.0" """));
        _feed.AddTheme("example.theme.untagged", "1.0.0", Manifest("1.0.0"), tags: "theme");
        var sut = new PluginFeedResolver(new NuGetPluginFeed(_feed.IndexUrl), new Version(1, 0), BundledPluginCatalog.Empty);
        var skipped = new List<string>();

        // Act
        var listed = await sut.ListAsync(TestContext.Current.CancellationToken);
        var choice = await sut.ChooseAsync(Id, skipped, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Id], listed.Select(package => package.Id));
        Assert.Equal("1.0.0", choice?.Version);
        Assert.Equal(["2.0.0: needs Invoklet 99.0.0 or newer."], skipped);
        Assert.NotEmpty(choice!.Package);
    }

    [Fact]
    public async Task ListAsync_TheFeedAnswers401_ReportsThatItRefusedAccess()
    {
        // Arrange
        _feed.RequireCredentials = true;
        var sut = new NuGetPluginFeed(_feed.IndexUrl);

        // Act
        var error = await Assert.ThrowsAsync<PluginFeedException>(() => sut.ListAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(PluginFeedError.Unauthorized, error.Error);
        Assert.DoesNotContain("127.0.0.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_NothingListens_ReportsTheFeedUnreachable()
    {
        // Arrange
        var address = _feed.IndexUrl;
        _feed.Dispose();
        var sut = new NuGetPluginFeed(address);

        // Act
        var error = await Assert.ThrowsAsync<PluginFeedException>(() => sut.ListAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(PluginFeedError.Unreachable, error.Error);
    }
}
