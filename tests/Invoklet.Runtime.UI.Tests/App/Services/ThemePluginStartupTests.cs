using System.IO.Compression;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Services;

[Trait("Category", "Integration")]
public sealed class ThemePluginStartupTests
{
    [AvaloniaFact]
    public void Startup_ApplicationOutput_DoesNotBundleCustomThemes()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        var packages = PluginDiscovery.Scan([new PluginSource(root, PluginTrust.Bundled)]);
        Assert.DoesNotContain(packages, package => package.Id is
            "invoklet.theme.vscode" or "invoklet.theme.rider" or "invoklet.theme.vs2022");
    }

    [AvaloniaFact]
    public void PrepareArchive_PathOutsidePackage_IsRejectedBeforeApproval()
    {
        var temp = Directory.CreateTempSubdirectory("Invoklet-theme-archive-").FullName;
        try
        {
            var archivePath = Path.Combine(temp, "invalid.invplugin");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
                writer.Write("outside the package");

            var store = new PluginInventoryStore(Path.Combine(temp, PluginInventory.FileName));
            var installer = new PluginPackageInstaller(Path.Combine(temp, "plugins"), store, BundledPluginCatalog.Empty);

            Assert.Throws<IOException>(() => installer.PrepareArchive(archivePath));
            Assert.False(File.Exists(Path.Combine(temp, "outside.txt")));
            Assert.Empty(store.Read().Entries);
            Assert.Empty(Directory.EnumerateDirectories(temp));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(".invplugin")]
    [InlineData(".nupkg")]
    public void Startup_ThemeInstalledFromPreferences_AppliesApprovalToggleAndRemovalOnNextStart(string? extension)
    {
        using var source = new ThemePackageFixture();
        source.Write();
        var temp = Directory.CreateTempSubdirectory("Invoklet-installed-theme-").FullName;
        try
        {
            var user = Path.Combine(temp, "plugins");
            var store = new PluginInventoryStore(Path.Combine(temp, PluginInventory.FileName));
            var installer = new PluginPackageInstaller(user, store, BundledPluginCatalog.Empty);
            var archive = Path.Combine(temp, "ocean" + extension);
            if (extension == ".nupkg")
            {
                using var packageArchive = ZipFile.Open(archive, ZipArchiveMode.Create);
                foreach (var file in Directory.EnumerateFiles(source.PackageDirectory))
                    packageArchive.CreateEntryFromFile(file, "plugin/" + Path.GetFileName(file));
                packageArchive.CreateEntry("example.theme.ocean.nuspec");
            }
            else if (extension is not null) ZipFile.CreateFromDirectory(source.PackageDirectory, archive);

            var initial = Load();
            using (var cancelled = Prepare())
                Assert.Equal(ThemePackageFixture.Id, cancelled.Manifest.Id);
            Assert.Empty(store.Read().Entries);
            Assert.False(Directory.Exists(user));
            Assert.Empty(Directory.EnumerateDirectories(temp, ".plugin-install-*"));

            if (extension == ".nupkg")
            {
                var previousBundle = new BundledPluginCatalog(
                    [new BundledPluginEntry(ThemePackageFixture.Id, "1.0.0", new string('a', 64))]);
                store.SetEnabled(ThemePackageFixture.Id, false, previousBundle);
            }

            using var package = Prepare();
            installer.Install(package);
            Assert.False(File.Exists(Path.Combine(user, ThemePackageFixture.Id, "1.0.0", "example.theme.ocean.nuspec")));
            Assert.Empty(initial.Session.Loaded);
            Assert.Contains(ThemePackageFixture.Id, initial.Session.GetPendingRestart(store.Read()));
            var enabled = Load();
            Assert.Equal(PluginTrust.Trusted, Assert.Single(enabled.Session.Loaded).Trust);
            Assert.Contains(enabled.Catalog.Interfaces, theme => theme.Id == ThemePackageFixture.Id);

            store.SetEnabled(ThemePackageFixture.Id, false, BundledPluginCatalog.Empty);
            Assert.Contains(ThemePackageFixture.Id, enabled.Session.GetPendingRestart(store.Read()));
            var disabled = Load();
            Assert.Empty(disabled.Session.Loaded);
            Assert.Contains(Assert.Single(disabled.Session.Rejected).Diagnostics, item => item.Code == PluginDiagnosticCode.PluginDisabled);
            store.SetEnabled(ThemePackageFixture.Id, true, BundledPluginCatalog.Empty);
            Assert.Empty(enabled.Session.GetPendingRestart(store.Read()));
            Assert.Single(Load().Session.Loaded);

            installer.Remove(ThemePackageFixture.Id);
            Assert.Single(enabled.Session.Loaded);
            Assert.Empty(store.Read().Entries);
            Assert.Contains(ThemePackageFixture.Id, enabled.Session.GetPendingRestart(store.Read()));
            var removed = Load();
            Assert.Empty(removed.Session.Loaded);
            Assert.Empty(removed.Session.Rejected);
            Assert.DoesNotContain(removed.Catalog.Interfaces, theme => theme.Id == ThemePackageFixture.Id);

            PreparedPluginPackage Prepare() => extension is null ? installer.Prepare(source.PackageDirectory) : installer.PrepareArchive(archive);

            (ThemeCatalog Catalog, PluginSession Session) Load()
            {
                var catalog = new ThemeCatalog();
                var session = new PluginLoader(new PluginLoaderOptions
                {
                    ApplicationVersion = new Version(1, 0),
                    UserDirectory = user,
                    Inventory = store.Read(),
                    NativeLibraryCacheDirectory = Path.Combine(temp, "native"),
                }, [catalog], NullLoggerFactory.Instance).Load();
                return (catalog, session);
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData("vscode")]
    [InlineData("rider")]
    [InlineData("vs2022")]
    public void Startup_BundledThemePackage_TogglesBothPalettes(string family)
    {
        var root = TestInterfaceThemes.PackageDirectory;
        var bundled = BundledPluginCatalog.Read(Path.Combine(root, BundledPluginCatalog.FileName));
        var temp = Directory.CreateTempSubdirectory("Invoklet-theme-state-").FullName;
        var id = $"invoklet.theme.{family}";
        var darkId = id + "-dark";
        var lightId = id + "-light";
        try
        {
            var store = new PluginInventoryStore(Path.Combine(temp, PluginInventory.FileName));
            var initial = Load();
            Assert.Equal(3, initial.Session.Loaded.Count);
            Assert.Single(initial.Session.Loaded, item => item.Id == id);
            Assert.Contains(initial.Catalog.Interfaces, item => item.Id == darkId && item.IsDark);
            Assert.Contains(initial.Catalog.Interfaces, item => item.Id == lightId && !item.IsDark);

            store.SetEnabled(id, false, bundled);
            Assert.Contains(initial.Catalog.Interfaces, item => item.Id == darkId);
            Assert.Contains(initial.Catalog.Interfaces, item => item.Id == lightId);
            Assert.Contains(id, initial.Session.GetPendingRestart(store.Read()));
            var disabled = Load();
            Assert.DoesNotContain(disabled.Catalog.Interfaces, item => item.Id == darkId || item.Id == lightId);
            Assert.Contains(disabled.Session.Rejected, item => item.Id == id
                && item.Diagnostics.Any(diagnostic => diagnostic.Code == PluginDiagnosticCode.PluginDisabled));
            Assert.Empty(disabled.Session.GetPendingRestart(store.Read()));

            store.SetEnabled(id, true, bundled);
            Assert.Contains(id, disabled.Session.GetPendingRestart(store.Read()));
            var reenabled = Load();
            Assert.Contains(reenabled.Catalog.Interfaces, item => item.Id == darkId);
            Assert.Contains(reenabled.Catalog.Interfaces, item => item.Id == lightId);

            (ThemeCatalog Catalog, PluginSession Session) Load()
            {
                var catalog = new ThemeCatalog();
                var session = new PluginLoader(new PluginLoaderOptions
                {
                    ApplicationVersion = new Version(1, 0),
                    BundledDirectory = root,
                    BundledCatalog = bundled,
                    Inventory = store.Read(),
                    NativeLibraryCacheDirectory = Path.Combine(temp, "native"),
                }, [catalog], NullLoggerFactory.Instance).Load();
                return (catalog, session);
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Startup_LegacyBundledThemeOptOut_StaysDisabledUntilThePairIsEnabled(string mode)
    {
        const string id = "invoklet.theme.vscode";
        var legacyId = id + "-" + mode;
        var root = TestInterfaceThemes.PackageDirectory;
        var bundled = BundledPluginCatalog.Read(Path.Combine(root, BundledPluginCatalog.FileName));
        var replacements = new Dictionary<string, string> { [legacyId] = id };
        var temp = Directory.CreateTempSubdirectory("Invoklet-legacy-theme-state-").FullName;
        try
        {
            var store = new PluginInventoryStore(Path.Combine(temp, PluginInventory.FileName));
            var legacy = new BundledPluginCatalog([new BundledPluginEntry(legacyId, "1.0.0", new string('a', 64))]);
            store.SetEnabled(legacyId, false, legacy);

            var disabled = Load();
            Assert.Contains(disabled.Rejected, item => item.Id == id
                && item.Diagnostics.Any(diagnostic => diagnostic.Code == PluginDiagnosticCode.PluginDisabled));
            Assert.Empty(disabled.GetPendingRestart(store.Read(bundled, replacements)));

            store.SetEnabled(id, true, bundled, replacements);
            Assert.Empty(store.Read().Entries);
            Assert.Equal([id], disabled.GetPendingRestart(store.Read(bundled, replacements)));
            Assert.True(Load().IsLoaded(id));

            PluginSession Load() => new PluginLoader(new PluginLoaderOptions
            {
                ApplicationVersion = new Version(1, 0),
                BundledDirectory = root,
                BundledCatalog = bundled,
                Inventory = store.Read(bundled, replacements),
                NativeLibraryCacheDirectory = Path.Combine(temp, "native"),
            }, [new ThemeCatalog()], NullLoggerFactory.Instance).Load();
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(false, 2, "invoklet.theme.midnight")]
    [InlineData(true, 8, "invoklet.theme.vscode-dark")]
    public void Startup_BundledLayout_UsesAvailablePalettesAndMigratesLegacySelection(bool bundled, int count, string activeId)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var settings = new AppearanceSettings
        {
            Mode = ThemeMode.Dark,
            DarkInterfaceTheme = "VS Code Dark",
            LightInterfaceTheme = "invoklet.theme.rider-light",
            DarkEditorTheme = "Monokai",
        };
        var preferences = Preferences(settings);
        var themes = bundled ? TestInterfaceThemes.All.Except(BuiltInInterfaceThemes.All).ToArray() : [];

        try
        {
            using var service = new ThemeService(app, preferences.Object, themes);

            Assert.Equal(count, service.InterfaceThemes.Count);
            Assert.Equal(activeId, service.CurrentTheme.Interface.Id);
            Assert.Equal("invoklet.theme.vscode-dark", settings.DarkInterfaceTheme);
            Assert.Equal("invoklet.theme.rider-light", settings.LightInterfaceTheme);
            Assert.Equal("textmate.monokai", settings.DarkEditorTheme);
            preferences.Verify(item => item.Save(), Times.Once);
            if (bundled)
            {
                Assert.Equal(["VS Code Dark", "Rider Dark", "VS 2022 Dark", "Invoklet Midnight",
                    "VS Code Light", "Rider Light", "VS 2022 Light", "Invoklet Warm Light"],
                    service.InterfaceThemes.Select(theme => theme.Name));
                var palette = service.CurrentTheme.Interface.Variant;
                Assert.True(app.TryGetResource("Invoklet.Sidebar.BackgroundBrush", palette, out var brush));
                Assert.Equal(Color.FromRgb(37, 37, 38), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
            }
        }
        finally { app.RequestedThemeVariant = previous; }
    }

    [AvaloniaFact]
    public void Startup_InvalidThemeDefinition_ReportsItsFieldInPluginDiagnostics()
    {
        using var package = new ThemePackageFixture();
        package.Write("""{"name":"Missing ID"}""");

        var loaded = package.Load();

        var rejected = Assert.Single(loaded.Session.Rejected);
        var diagnostic = Assert.Single(rejected.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.RegistrationFailed, diagnostic.Code);
        Assert.Equal("id: A theme id is required.", diagnostic.Message);
    }

    [AvaloniaFact]
    public void Startup_ThemeAddedAfterLoad_AppearsOnlyOnNextStartAndSuppliesAnEditorDefault()
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        using var package = new ThemePackageFixture();
        var settings = new AppearanceSettings
        {
            Mode = ThemeMode.Dark,
            DarkInterfaceTheme = ThemePackageFixture.Id,
            DarkEditorTheme = "Dark+",
            LightEditorTheme = "Light+",
        };
        var preferences = Preferences(settings);
        try
        {
            var before = package.Load();
            using (var service = new ThemeService(app, preferences.Object, before.Catalog.Interfaces, before.Catalog.Editors))
            {
                package.Write();
                Assert.Empty(before.Session.Loaded);
                Assert.Equal(2, service.InterfaceThemes.Count);
                Assert.Null(settings.DarkEditorTheme);
                Assert.Null(settings.LightEditorTheme);
            }

            var after = package.Load();
            using var restarted = new ThemeService(app, preferences.Object, after.Catalog.Interfaces, after.Catalog.Editors);
            var loaded = Assert.Single(after.Session.Loaded);
            Assert.Equal(ThemePackageFixture.Id, loaded.Id);
            Assert.Contains(loaded.Diagnostics, item => item.Code == PluginDiagnosticCode.ThemeAssetInvalid);
            Assert.Equal("Ocean (Example)", restarted.CurrentTheme.Interface.DisplayName);
            Assert.Equal(ThemePackageFixture.Id + "/ocean-code", restarted.CurrentTheme.Editor.Id);

            restarted.SetEditorTheme(restarted.EditorThemes.Single(theme => theme.Id == "textmate.monokai"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("textmate.monokai", restarted.CurrentTheme.Editor.Id);
            Assert.Equal("textmate.monokai", settings.DarkEditorTheme);
        }
        finally { app.RequestedThemeVariant = previous; }
    }

    private static Mock<IPreferencesService> Preferences(AppearanceSettings settings)
    {
        var preferences = new Mock<IPreferencesService>();
        preferences.Setup(item => item.Appearance).Returns(settings);
        return preferences;
    }
}

internal sealed class ThemePackageFixture : IDisposable
{
    public const string Id = "example.theme.ocean";
    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-theme-package-").FullName;
    public string PackageDirectory => Path.Combine(_root, Id);

    public void Write(string? definition = null)
    {
        var package = Directory.CreateDirectory(PackageDirectory).FullName;
        File.WriteAllText(Path.Combine(package, "invoklet-plugin.json"), """
            {"id":"example.theme.ocean","version":"1.0.0","displayName":"Ocean","publisher":"Example",
             "roles":{"theme":1},"assets":{"themes":["ocean.json"]}}
            """);
        File.WriteAllText(Path.Combine(package, "ocean.json"), definition ?? """
            {"id":"example.theme.ocean","name":"Ocean","extends":"invoklet.theme.midnight",
             "colors":{"Invoklet.Sidebar.BackgroundBrush":"#162B36"},
             "editor":{"default":"ocean-code","themes":{"ocean-code":"code.json","broken":"broken.json"}}}
            """);
        File.WriteAllText(Path.Combine(package, "code.json"), """
            {"tokenColors":[{"scope":"storage.type","settings":{"foreground":"#13B58A"}}]}
            """);
        File.WriteAllText(Path.Combine(package, "broken.json"), "{");
    }

    public (ThemeCatalog Catalog, PluginSession Session) Load()
    {
        var catalog = new ThemeCatalog();
        var package = PackageDirectory;
        var inventory = Directory.Exists(package)
            ? new PluginInventory([new PluginInventoryEntry(Id, "1.0.0", ["theme"], "local", "Example",
                PluginContent.ComputeHash(package), "none", Enabled: true, Approved: true)])
            : PluginInventory.Empty;
        var session = new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = new Version(1, 0),
            UserDirectory = _root,
            Inventory = inventory,
            NativeLibraryCacheDirectory = Path.Combine(_root, "unused-native-cache"),
        }, [catalog], NullLoggerFactory.Instance).Load();
        return (catalog, session);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
