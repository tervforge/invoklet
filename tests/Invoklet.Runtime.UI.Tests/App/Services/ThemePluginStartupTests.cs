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
    [AvaloniaTheory]
    [InlineData(false, 2, "invoklet.theme.midnight")]
    [InlineData(true, 8, "invoklet.theme.vscode-dark")]
    public void Startup_BundledLayout_UsesAvailablePalettesAndMigratesLegacySelection(bool bundled, int count, string activeId)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var settings = new AppearanceSettings { Mode = ThemeMode.Dark, DarkInterfaceTheme = "VS Code Dark", DarkEditorTheme = "Monokai" };
        var preferences = Preferences(settings);
        var themes = bundled ? TestInterfaceThemes.All.Except(BuiltInInterfaceThemes.All).ToArray() : [];

        try
        {
            using var service = new ThemeService(app, preferences.Object, themes);

            Assert.Equal(count, service.InterfaceThemes.Count);
            Assert.Equal(activeId, service.CurrentTheme.Interface.Id);
            Assert.Equal("invoklet.theme.vscode-dark", settings.DarkInterfaceTheme);
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
    public void Startup_ThemeAddedAfterLoad_AppearsOnlyOnNextStartAndSuppliesAnEditorDefault()
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        using var package = new ThemePackageFixture();
        var settings = new AppearanceSettings { Mode = ThemeMode.Dark, DarkInterfaceTheme = ThemePackageFixture.Id };
        var preferences = Preferences(settings);
        try
        {
            var before = package.Load();
            using (var service = new ThemeService(app, preferences.Object, before.Catalog.Interfaces, before.Catalog.Editors))
            {
                package.Write();
                Assert.Empty(before.Session.Loaded);
                Assert.Equal(2, service.InterfaceThemes.Count);
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

    public void Write()
    {
        var package = Directory.CreateDirectory(Path.Combine(_root, Id)).FullName;
        File.WriteAllText(Path.Combine(package, "invoklet-plugin.json"), """
            {"id":"example.theme.ocean","version":"1.0.0","displayName":"Ocean","publisher":"Example",
             "roles":{"theme":1},"assets":{"themes":["ocean.json"]}}
            """);
        File.WriteAllText(Path.Combine(package, "ocean.json"), """
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
        var package = Path.Combine(_root, Id);
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
