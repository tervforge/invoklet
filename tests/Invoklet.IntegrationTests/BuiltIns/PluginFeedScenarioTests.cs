using Avalonia.Headless.XUnit;
using Invoklet.BuiltIns;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Services;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.IntegrationTests;

/// <summary>
/// The whole Plugins page flow with real packages over HTTP: browse the feed, install a theme, restart, disable, enable, remove and
/// restart again. The packages are the ones the plugins repository packs: set INVOKLET_PLUGIN_FEED to a feed that serves them (such as
/// the static feed its build/static_feed.py writes), or INVOKLET_PLUGIN_PACKAGES to a folder that holds them (searched recursively).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PluginFeedScenarioTests : IDisposable
{
    private const string Theme = "invoklet.theme.vscode";
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
        Assert.Contains(page.FeedPackages, row => row.Package.Id == Theme && row.CanInstall);
        Assert.DoesNotContain(page.FeedPackages, row => row.Package.Id.StartsWith("Invoklet.", StringComparison.Ordinal));

        var declined = false;
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == Theme), (_, _) => Task.FromResult(declined), cancel);
        Assert.Empty(Store.Read().Entries);
        await page.InstallFromFeedAsync(page.FeedPackages.Single(row => row.Package.Id == Theme), (manifest, _) =>
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

    // What the next start of Invoklet loads from the user folder and inventory.
    private (PluginSession Session, ThemeCatalog Catalog) Start()
    {
        var catalog = new ThemeCatalog();
        var session = new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = PluginStartup.ApplicationVersion,
            UserDirectory = UserDirectory,
            Inventory = Store.Read(),
            LoadThemePackages = true,
            NativeLibraryCacheDirectory = Path.Combine(_root, "native"),
        }, [catalog], NullLoggerFactory.Instance).Load();
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
