using System.IO.Compression;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.IntegrationTests;

// A third-party connector through the plugin host: installed from a NuGet package, approved, loaded at the next start and removed.
[Trait("Category", "Integration")]
public sealed class SampleConnectorLoadingTests
{
    [Fact]
    public void Installer_ConnectorCanBeApprovedAndRemoved()
    {
        const string id = "example.connector.http";
        using var package = new SampleConnectorPackage();
        var installer = new PluginPackageInstaller(package.UserDirectory, package.Inventory, BundledPluginCatalog.Empty);
        var path = package.CreatePackage();
        PreparedPluginPackage Prepare() => installer.PrepareArchive(path);

        using (var preview = Prepare())
            Assert.True(preview.Manifest.RequiresCode);
        Assert.Empty(package.Inventory.Read().Entries);

        var registry = new ConnectionDriverRegistry();
        var current = Load(registry, package.Inventory.Read(), package.UserDirectory);
        using (var approved = Prepare())
            installer.Install(approved);
        Assert.Null(registry.Find("sample-http"));
        Assert.Contains(id, current.GetPendingRestart(package.Inventory.Read()));
        Assert.Equal(["connection"], Assert.Single(package.Inventory.Read().Entries).Roles);

        var restarted = Load(new ConnectionDriverRegistry(), package.Inventory.Read(), package.UserDirectory);
        Assert.True(restarted.IsLoaded(id));
        installer.Remove(id);
        Assert.Empty(package.Inventory.Read().Entries);
        Assert.Contains(id, restarted.GetPendingRestart(package.Inventory.Read()));
        Assert.False(Load(new ConnectionDriverRegistry(), package.Inventory.Read(), package.UserDirectory).IsLoaded(id));
    }

    [Fact]
    public void Installer_ApprovedConnector_RunsNoPluginCodeUntilTheNextStart()
    {
        using var package = new SampleConnectorPackage();
        var marker = Path.Combine(package.UserDirectory + "-marker", "registered");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var installer = new PluginPackageInstaller(package.UserDirectory, package.Inventory, BundledPluginCatalog.Empty);
        AppContext.SetData("SampleHttpConnector.RegisterMarker", marker);
        try
        {
            using (var prepared = installer.PrepareArchive(package.CreatePackage()))
                installer.Install(prepared);
            Assert.False(File.Exists(marker));

            Load(new ConnectionDriverRegistry(), package.Inventory.Read(), package.UserDirectory);

            Assert.True(File.Exists(marker));
        }
        finally { AppContext.SetData("SampleHttpConnector.RegisterMarker", null); }
    }

    [Fact]
    public void UserConnector_RegistersOnlyAfterContentApproval()
    {
        const string id = "example.connector.http";
        var root = Directory.CreateTempSubdirectory("Invoklet-external-connector");
        try
        {
            var package = root.CreateSubdirectory(id);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "SampleHttpConnector.dll"), Path.Combine(package.FullName, "SampleHttpConnector.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "sample-http-connector.json"), Path.Combine(package.FullName, PluginContracts.ManifestFileName));
            var entry = new PluginInventoryEntry(id, "1.0.0", ["connection"], "test", null,
                PluginContent.ComputeHash(package.FullName), "none", Enabled: true, Approved: false);
            var registry = new ConnectionDriverRegistry();

            var unapproved = Load(registry, new PluginInventory([entry]), root.FullName);

            Assert.Null(registry.Find("sample-http"));
            Assert.Contains(Assert.Single(unapproved.Rejected).Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCode.PluginNotApproved);

            registry = new ConnectionDriverRegistry();
            var approved = Load(registry, new PluginInventory([entry with { Approved = true }]), root.FullName);

            Assert.Empty(approved.Rejected);
            Assert.Equal(PluginTrust.Trusted, approved.Find(id)?.Trust);
            Assert.IsAssignableFrom<ICSharpClientSource>(registry.Find("sample-http"));
            var connection = new MdRunConnection
            {
                Name = "Sample",
                Driver = "sample-http",
                Params = new() { ["baseAddress"] = MdRunValue.Of("https://example.test/") },
            };
            var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, [], registry)!;
            Assert.Empty(generated.Errors);
            Assert.Contains("SampleClient", generated.Source);
            Assert.Equal(["https://example.test/"], generated.StartupValues);
            Assert.DoesNotContain("https://example.test/", generated.Source);
        }
        finally { root.Delete(recursive: true); }
    }

    private static PluginSession Load(ConnectionDriverRegistry registry, PluginInventory inventory, string userDirectory) =>
        new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = new Version(1, 0),
            Inventory = inventory,
            UserDirectory = userDirectory,
            NativeLibraryCacheDirectory = Path.Combine(AppContext.BaseDirectory, "connector-native-cache"),
        }, [new ConnectionPluginRoleHandler(registry)], NullLoggerFactory.Instance).Load();
}

internal sealed class SampleConnectorPackage : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("Invoklet-connector-install");
    public string UserDirectory => Path.Combine(_root.FullName, "plugins");
    public PluginInventoryStore Inventory => new(Path.Combine(_root.FullName, PluginInventory.FileName));

    /// <summary>Packs the sample connector as a NuGet package with its files under <c>plugin/</c>.</summary>
    public string CreatePackage(string? dependency = null)
    {
        var source = _root.CreateSubdirectory("source").FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SampleHttpConnector.dll"), Path.Combine(source, "SampleHttpConnector.dll"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "sample-http-connector.json"), Path.Combine(source, PluginContracts.ManifestFileName));
        if (dependency is not null)
        {
            var manifestPath = Path.Combine(source, PluginContracts.ManifestFileName);
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["dependsOn"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create(dependency));
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        }

        var archive = Path.Combine(_root.FullName, "sample.nupkg");
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(source))
            zip.CreateEntryFromFile(file, "plugin/" + Path.GetFileName(file));
        return archive;
    }

    public void Dispose() => _root.Delete(recursive: true);
}
