using System.IO.Compression;
using System.Text;
using Invoklet.Plugins.Hosting;
using Moq;

namespace Invoklet.NonUI.Tests.Plugins.Fixtures;

/// <summary>A mocked feed and in-memory <c>.nupkg</c> files for <see cref="PluginFeedResolver"/>.</summary>
public sealed class PluginFeedResolverFixture
{
    public const string Id = "example.theme.ocean";

    public Mock<IPluginFeed> Feed { get; } = new(MockBehavior.Strict);

    public PluginFeedResolver CreateSut(BundledPluginCatalog? bundled = null) =>
        new(Feed.Object, new Version(1, 0), bundled ?? BundledPluginCatalog.Empty);

    /// <summary>Makes the feed list <paramref name="versions"/>, newest first, each with the manifest <paramref name="manifest"/> gives.</summary>
    public void Offer(IReadOnlyList<string> versions, Func<string, string?> manifest)
    {
        Feed.Setup(feed => feed.GetVersionsAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(versions);
        foreach (var version in versions)
            Feed.Setup(feed => feed.DownloadAsync(Id, version, It.IsAny<CancellationToken>())).ReturnsAsync(Package(manifest(version)));
    }

    public static string Theme(string version, string extra = "") =>
        $$"""{"id":"{{Id}}","version":"{{version}}","displayName":"Ocean","roles":{"theme":1},"assets":{"themes":["t.json"]}{{extra}}}""";

    /// <summary>A package with the manifest under <c>plugin/</c>; none at all when <paramref name="manifest"/> is null.</summary>
    public static byte[] Package(string? manifest)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var nuspec = archive.CreateEntry(Id + ".nuspec").Open())
                nuspec.Write("<package/>"u8);
            if (manifest is not null)
            {
                using var entry = archive.CreateEntry("plugin/invoklet-plugin.json").Open();
                entry.Write(Encoding.UTF8.GetBytes(manifest));
            }
        }
        return stream.ToArray();
    }
}
