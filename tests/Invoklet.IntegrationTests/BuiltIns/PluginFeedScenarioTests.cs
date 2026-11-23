using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Invoklet.BuiltIns;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Services;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.IntegrationTests;

/// <summary>
/// Exercises the Plugins page with real packages over HTTP. Set INVOKLET_PLUGIN_FEED to a feed that serves them.
/// For the theme scenario, INVOKLET_PLUGIN_PACKAGES can point to a local package folder instead.
/// The SQLite scenario uses sqlite3 to create the fixture and the .NET SDK to run a script.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PluginFeedScenarioTests : IDisposable
{
    private const string Theme = "invoklet.theme.vscode";
    private const string ThemePackage = "tervforge." + Theme;
    private readonly StaticPluginFeed _feed = new();
    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-feed-scenario-").FullName;
    private string _feedUrl = "";

    public void Dispose()
    {
        _feed.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string UserDirectory => Path.Combine(_root, "plugins");
    private PluginInventoryStore Store => new(Path.Combine(_root, PluginInventory.FileName));

    [AvaloniaFact]
    public async Task FeedTheme_InstalledApprovedDisabledEnabledAndRemoved_TakesEffectAtEachRestart()
    {
        var feed = Environment.GetEnvironmentVariable(PluginStartup.PluginFeedVariable);
        var packages = Environment.GetEnvironmentVariable("INVOKLET_PLUGIN_PACKAGES");
        Assert.SkipWhen(string.IsNullOrEmpty(feed) && string.IsNullOrEmpty(packages),
            "Neither INVOKLET_PLUGIN_FEED nor INVOKLET_PLUGIN_PACKAGES is set.");
        if (string.IsNullOrEmpty(feed))
        {
            foreach (var file in Directory.EnumerateFiles(packages!, "*.nupkg", SearchOption.AllDirectories))
                _feed.AddPackage(File.ReadAllBytes(file));
            feed = _feed.IndexUrl;
        }
        _feedUrl = feed;
        var cancel = TestContext.Current.CancellationToken;

        var (session, _) = Start();
        var page = Page(session);
        await page.BrowseAsync(cancel);
        Assert.Null(page.FeedStatus);
        Assert.Contains(page.FeedPackages, row => row.Package.Id == ThemePackage && row.CanInstall);
        Assert.DoesNotContain(page.FeedPackages, row => row.Package.Id.StartsWith("Invoklet.", StringComparison.Ordinal));

        var declined = false;
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == ThemePackage), (_, _) => Task.FromResult(declined), cancel);
        Assert.Empty(Store.Read().Entries);
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == ThemePackage), (manifest, _) =>
            Task.FromResult(manifest.Id == Theme), cancel);
        Assert.True(page.HasPendingRestart);
        Assert.False(session.IsLoaded(Theme));

        (session, var catalog) = Start();
        Assert.True(session.IsLoaded(Theme));
        Assert.Contains(catalog.Interfaces, palette => palette.Id == "invoklet.theme.vscode-dark");

        page = Page(session);
        page.Toggle(page.Rows.Single(row => row.Id == Theme));
        Assert.True(page.HasPendingRestart);
        (session, catalog) = Start();
        Assert.Contains(session.Rejected.Single(plugin => plugin.Id == Theme).Diagnostics,
            diagnostic => diagnostic.Code == PluginDiagnosticCode.PluginDisabled);
        Assert.DoesNotContain(catalog.Interfaces, palette => palette.Id == "invoklet.theme.vscode-dark");

        page = Page(session);
        page.Toggle(page.Rows.Single(row => row.Id == Theme));
        (session, _) = Start();
        Assert.True(session.IsLoaded(Theme));

        page = Page(session);
        await page.RemoveAsync(page.Rows.Single(row => row.Id == Theme), () => Task.FromResult(true));
        Assert.True(page.HasPendingRestart);
        (session, catalog) = Start();
        Assert.False(session.IsLoaded(Theme));
        Assert.DoesNotContain(catalog.Interfaces, palette => palette.Id == "invoklet.theme.vscode-dark");
    }

    [AvaloniaFact]
    public async Task FeedSqlite_OffersEfCoreAndRunsQueryAfterApprovalAndRestart()
    {
        const string sqlite = "invoklet.connector.sqlite";
        const string efCore = "invoklet.connector.efcore";
        var feed = Environment.GetEnvironmentVariable(PluginStartup.PluginFeedVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "INVOKLET_PLUGIN_FEED is not set.");
        _feedUrl = feed!;
        var cancel = TestContext.Current.CancellationToken;
        var (before, _) = Start();
        var page = Page(before);
        await page.BrowseAsync(cancel);
        Assert.Null(page.FeedStatus);
        var approvals = new List<string>();
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == "tervforge." + sqlite), (manifest, _) =>
        {
            approvals.Add(manifest.Id);
            return Task.FromResult(manifest.Id == efCore);
        }, cancel);
        Assert.Equal([efCore, sqlite], approvals);
        Assert.Empty(Store.Read().Entries);

        approvals.Clear();
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == "tervforge." + sqlite), (manifest, _) =>
        {
            approvals.Add(manifest.Id);
            return Task.FromResult(true);
        }, cancel);
        Assert.Equal([efCore, sqlite], approvals);
        Assert.True(page.HasPendingRestart);
        Assert.False(before.IsLoaded(sqlite));

        var registry = BuiltInCatalog.CreateConnectionDrivers();
        var (after, _) = Start(registry);
        Assert.Empty(after.Rejected.SelectMany(plugin => plugin.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(after.IsLoaded(efCore));
        Assert.True(after.IsLoaded(sqlite));
        var driver = Assert.IsAssignableFrom<IConnectionDriver>(registry.Find("sqlite"));
        var file = Path.Combine(_root, "sample.db");
        using var setup = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sqlite3")
        {
            ArgumentList = { file, "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO products VALUES (1, 'Sample');" },
            UseShellExecute = false,
        })!;
        await setup.WaitForExitAsync(cancel);
        Assert.Equal(0, setup.ExitCode);

        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = "sqlite",
            Params = new(StringComparer.Ordinal) { ["file"] = MdRunValue.Of(file) },
        };
        var schemas = new ConnectionSchemas(registry, Path.Combine(_root, "schemas"), DotnetConnectionTools.Default, _ => null);
        await schemas.EnsureAsync([connection], _root, ConnectionSchemaRefresh.Missing, cancel);
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, registry, schemas.Cache, _root);
        Assert.NotNull(generated);
        Assert.Empty(generated.Errors);

        var buildDirectory = Directory.CreateDirectory(Path.Combine(_root, "script-build")).FullName;
        var options = CompiledDotnetLanguageHostOptions.From(new ExecutionRequest
        {
            Platform = new DotnetSdkProvider().Get().First(),
            OutputDirectory = buildDirectory,
            WorkingDirectory = _root,
            CurrentQueryPath = Path.Combine(_root, "shop.inv"),
            Packages = driver.Packages,
            Connections = generated,
        }, new CSharpReferenceService().GetExtensionReference());
        await using var host = new CSharpLanguageHost(options, NullLoggerFactory.Instance);
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        const string script = """
            using Microsoft.EntityFrameworkCore;
            var result = Connections.Shop.Database.SqlQueryRaw<int>("SELECT 1 AS Value FROM products").Single();
            Console.WriteLine($"Query result: {result}");
            """;
        var run = await host.RunAsync(script, (data, kind) => (kind == OutputKind.StandardError ? errors : output).Enqueue(data), cancel)
            .WaitAsync(TimeSpan.FromMinutes(2), cancel);
        Assert.True(run.Succeeded, string.Join('\n', errors.Concat(output)));
        Assert.Contains("Query result: 1", output);
    }

    // What the next start of Invoklet loads from the user folder and inventory.
    private (PluginSession Session, ThemeCatalog Catalog) Start(ConnectionDriverRegistry? registry = null)
    {
        var catalog = new ThemeCatalog();
        var session = new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = PluginStartup.ApplicationVersion,
            UserDirectory = UserDirectory,
            Inventory = Store.Read(),
            LoadThemePackages = true,
            NativeLibraryCacheDirectory = Path.Combine(_root, "native"),
        }, [catalog, new ConnectionPluginRoleHandler(registry ?? BuiltInCatalog.CreateConnectionDrivers())], NullLoggerFactory.Instance).Load();
        return (session, catalog);
    }

    private PluginsPageViewModel Page(PluginSession session)
    {
        var page = new PluginsPageViewModel(session, Store.Read, () => Task.CompletedTask,
            setEnabled: (id, enabled) => Store.SetEnabled(id, enabled, BundledPluginCatalog.Empty, session: session, userDirectory: UserDirectory),
            createInstaller: () => new PluginPackageInstaller(UserDirectory, Store, BundledPluginCatalog.Empty),
            createFeed: () => new PluginFeedResolver(new NuGetPluginFeed(_feedUrl), PluginStartup.ApplicationVersion, BundledPluginCatalog.Empty));
        page.Refresh();
        return page;
    }
}
