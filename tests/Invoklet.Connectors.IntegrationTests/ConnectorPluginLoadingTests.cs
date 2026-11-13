using System.IO.Compression;
using System.Runtime.Loader;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ConnectorPluginLoadingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(".invplugin")]
    [InlineData(".nupkg")]
    public void Installer_ConnectorCanBeApprovedAndRemoved(string? extension)
    {
        const string id = "example.connector.http";
        using var package = new SampleConnectorPackage();
        var installer = new PluginPackageInstaller(package.UserDirectory, package.Inventory, BundledPluginCatalog.Empty);
        var path = package.CreateSource(extension);
        PreparedPluginPackage Prepare() => extension is null ? installer.Prepare(path) : installer.PrepareArchive(path);

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
                Name = "Sample", Driver = "sample-http",
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

    [Fact]
    public void PublishedConnectors_RegisterAllDriversAndShareTheEfCorePlugin()
    {
        var registry = new ConnectionDriverRegistry();
        var session = Load(registry);

        Assert.Empty(session.Rejected);
        Assert.Equal(6, session.Loaded.Count);
        Assert.Equal(["grpc", "http", "openapi", "postgres", "sqlite", "sqlserver"],
            registry.Drivers.Select(driver => driver.Id).Order(StringComparer.Ordinal));
        Assert.All(registry.Drivers, driver => Assert.IsAssignableFrom<ICSharpClientSource>(driver));
        var sqlite = registry.Find("sqlite")!;
        var postgres = registry.Find("postgres")!;
        Assert.Equal("Invoklet plugin: invoklet.connector.sqlite", AssemblyLoadContext.GetLoadContext(sqlite.GetType().Assembly)?.Name);
        Assert.Same(sqlite.GetType().BaseType, postgres.GetType().BaseType);
        Assert.Equal("Invoklet plugin: invoklet.connector.efcore",
            AssemblyLoadContext.GetLoadContext(sqlite.GetType().BaseType!.Assembly)?.Name);
    }

    [Fact]
    public async Task PublishedSqlite_ReadsSchemaThroughThePluginContext()
    {
        var registry = new ConnectionDriverRegistry();
        var session = Load(registry);
        Assert.Empty(session.Rejected);
        var driver = Assert.IsAssignableFrom<IConnectionSchemaDriver>(registry.Find("sqlite"));
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("Invoklet-plugin-sqlite");
        try
        {
            var file = Path.Combine(folder.FullName, "demo.db");
            await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
            {
                await database.OpenAsync(cancellationToken);
                await using var command = database.CreateCommand();
                command.CommandText = "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var schema = await driver.FetchSchemaAsync(new Dictionary<string, MdRunValue> { ["file"] = MdRunValue.Of(file) },
                new ConnectionTools { GetPackageFolderAsync = (_, _, _) => throw new InvalidOperationException("SQLite requires no schema tools.") },
                cancellationToken);
            Assert.Contains(schema.Files, source => source.Text.Contains("Products", StringComparison.Ordinal));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void DisabledBundledConnector_IsOmittedOnNextStart()
    {
        var folder = Directory.CreateTempSubdirectory("Invoklet-connector-inventory");
        try
        {
            const string id = "invoklet.connector.http";
            var store = new PluginInventoryStore(Path.Combine(folder.FullName, PluginInventory.FileName));
            store.SetEnabled(id, false, BundledCatalog(), bundledRoles: ["connection"]);
            Assert.Equal(["connection"], Assert.Single(store.Read().Entries).Roles);
            var registry = new ConnectionDriverRegistry();

            var session = Load(registry, store.Read());

            Assert.Null(registry.Find("http"));
            Assert.Null(registry.Find("openapi"));
            Assert.Contains(Assert.Single(session.Rejected).Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCode.PluginDisabled);
            Assert.NotNull(registry.Find("sqlite"));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void DuplicateOpenApiDriver_RollsBackHttpWithoutRemovingTheExistingDriver()
    {
        var registry = new ConnectionDriverRegistry();
        var existing = new Invoklet.Connectors.Http.OpenApiConnectionDriver();
        registry.Register(existing);

        var session = Load(registry);

        var rejected = Assert.Single(session.Rejected);
        Assert.Equal("invoklet.connector.http", rejected.Id);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCode.RegistrationFailed);
        Assert.Null(registry.Find("http"));
        Assert.Same(existing, registry.Find("openapi"));
        Assert.NotNull(registry.Find("grpc"));
    }

    private static string PackageDirectory => Path.Combine(AppContext.BaseDirectory, "connector-fixtures", "plugins");

    private static BundledPluginCatalog BundledCatalog() =>
        BundledPluginCatalog.Read(Path.Combine(PackageDirectory, BundledPluginCatalog.FileName));

    private static PluginSession Load(ConnectionDriverRegistry registry, PluginInventory? inventory = null, string? userDirectory = null) =>
        new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = new Version(1, 0),
            BundledDirectory = PackageDirectory,
            BundledCatalog = BundledCatalog(),
            Inventory = inventory ?? PluginInventory.Empty,
            UserDirectory = userDirectory,
            NativeLibraryCacheDirectory = Path.Combine(AppContext.BaseDirectory, "connector-native-cache"),
        }, [new ConnectionPluginRoleHandler(registry)], NullLoggerFactory.Instance).Load();
}

internal sealed class SampleConnectorPackage : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("Invoklet-connector-install");
    public string UserDirectory => Path.Combine(_root.FullName, "plugins");
    public PluginInventoryStore Inventory => new(Path.Combine(_root.FullName, PluginInventory.FileName));

    public string CreateSource(string? extension)
    {
        var source = _root.CreateSubdirectory("source").FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SampleHttpConnector.dll"), Path.Combine(source, "SampleHttpConnector.dll"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "sample-http-connector.json"), Path.Combine(source, PluginContracts.ManifestFileName));
        if (extension is null) return source;

        var archive = Path.Combine(_root.FullName, "sample" + extension);
        if (extension == ".nupkg")
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(source))
                zip.CreateEntryFromFile(file, "plugin/" + Path.GetFileName(file));
        }
        else ZipFile.CreateFromDirectory(source, archive);
        return archive;
    }

    public void Dispose() => _root.Delete(recursive: true);
}
