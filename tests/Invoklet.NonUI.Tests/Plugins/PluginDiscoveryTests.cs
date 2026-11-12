using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Category", "Integration")]
public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-plugin-discovery").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Source(string name) => Path.Combine(_root, name);

    private void WriteManifest(string relativeFolder, string id)
    {
        var folder = Path.Combine(_root, relativeFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PluginContracts.ManifestFileName),
            $$"""{ "id": "{{id}}", "version": "1.0.0", "displayName": "{{id}}", "roles": { "theme": 1 } }""");
    }

    [Fact]
    public void Scan_Should_FindManifestsOneAndTwoLevelsDown_InIdOrder()
    {
        WriteManifest("app/themes/zeta", "invoklet.theme.zeta");
        WriteManifest("app/alpha", "invoklet.theme.alpha");
        WriteManifest("app/connectors/beta", "invoklet.connector.beta");
        WriteManifest("app/connectors/beta/nested", "invoklet.connector.nested"); // under a plugin: not a plugin
        WriteManifest("app/too/deep/gamma", "invoklet.theme.gamma"); // three levels: not scanned

        var found = PluginDiscovery.Scan([new PluginSource(Source("app"), PluginTrust.Bundled)]);

        Assert.Equal(["invoklet.connector.beta", "invoklet.theme.alpha", "invoklet.theme.zeta"], found.Select(p => p.Id));
        Assert.All(found, p => Assert.Equal(PluginTrust.Bundled, p.Source.Trust));
        Assert.All(found, p => Assert.True(p.Manifest is not null));
    }

    [Fact]
    public void Scan_Should_ContinueAfterAnUnreadableManifestPath()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "broken", PluginContracts.ManifestFileName));
        WriteManifest("app/good", "invoklet.theme.good");

        var found = PluginDiscovery.Scan([new PluginSource(Source("app"), PluginTrust.Bundled)]);

        Assert.Equal("invoklet.theme.good", Assert.Single(found, plugin => plugin.Manifest is not null).Id);
        var rejected = Assert.Single(found, plugin => plugin.Manifest is null);
        Assert.Equal("broken", rejected.Id);
        Assert.Equal(PluginDiagnosticCode.ManifestUnreadable, Assert.Single(rejected.Diagnostics).Code);
    }

    [Fact]
    public void Scan_Should_KeepSourcesInTheOrderGiven_BeforeAnyPathOrder()
    {
        WriteManifest("user/aaa", "invoklet.theme.user");
        WriteManifest("app/zzz", "invoklet.theme.app");

        var found = PluginDiscovery.Scan(
        [
            new PluginSource(Source("app"), PluginTrust.Bundled),
            new PluginSource(Source("user"), PluginTrust.Trusted),
        ]);

        Assert.Equal(["invoklet.theme.app", "invoklet.theme.user"], found.Select(p => p.Id));
        Assert.Equal([PluginTrust.Bundled, PluginTrust.Trusted], found.Select(p => p.Source.Trust));
    }

    [Fact]
    public void Scan_Should_TreatAMissingSourceAsEmpty()
    {
        var found = PluginDiscovery.Scan([new PluginSource(Source("absent"), PluginTrust.Trusted)]);

        Assert.Empty(found);
    }

    [Fact]
    public void Scan_Should_ReportAnUnreadableManifest_UnderItsFolderName()
    {
        var folder = Path.Combine(_root, "user", "broken-plugin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PluginContracts.ManifestFileName), "{ not json");

        var found = PluginDiscovery.Scan([new PluginSource(Source("user"), PluginTrust.Trusted)]);

        var plugin = Assert.Single(found);
        Assert.Equal("broken-plugin", plugin.Id);
        Assert.Null(plugin.Manifest);
        Assert.Equal(folder, plugin.Directory);
        Assert.Equal(PluginDiagnosticCode.ManifestUnreadable, Assert.Single(plugin.Diagnostics).Code);
    }

    [Fact]
    public void ReadFolder_Should_ReadOnePluginFolder()
    {
        WriteManifest("dev/mine", "invoklet.theme.mine");

        var plugin = PluginDiscovery.ReadFolder(Path.Combine(_root, "dev", "mine"), new PluginSource(Source("dev"), PluginTrust.Development));

        Assert.Equal("invoklet.theme.mine", plugin.Id);
        Assert.Equal(PluginTrust.Development, plugin.Source.Trust);
    }
}
