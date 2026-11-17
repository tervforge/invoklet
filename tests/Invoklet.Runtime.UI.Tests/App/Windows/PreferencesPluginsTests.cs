using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Windows;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

public sealed class PreferencesPluginsTests
{
    private readonly PreferencesPluginsFixture _fixture = new();

    [AvaloniaFact]
    public void InterfacePage_ShowsTheMissingSavedThemeAndKeepsItsSelection()
    {
        var appearance = new AppearanceSettings { LightInterfaceTheme = "invoklet.theme.vscode-light" };
        _fixture.Preferences.SetupGet(service => service.Appearance).Returns(appearance);
        var fallback = BuiltInInterfaceThemes.All.Single(theme => !theme.IsDark);
        _fixture.Theme.SetupGet(service => service.CurrentTheme).Returns(new ActiveTheme(
            fallback, new EditorTheme("Light+", ThemeName.Light, false)));
        using var model = new PreferencesWindowViewModel(_fixture.Theme.Object, _fixture.Preferences.Object, new LanguageHostRegistry());
        var window = new PreferencesWindow(model);
        window.Show();
        try
        {
            model.SelectedNode = model.Nodes.Single(node => node.Id == "interface");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible
                && block.Text is { } message && message.Contains("invoklet.theme.vscode-light", StringComparison.Ordinal)
                && message.Contains(fallback.DisplayName, StringComparison.Ordinal));
            Assert.Equal("invoklet.theme.vscode-light", appearance.LightInterfaceTheme);
            _fixture.Theme.Verify(service => service.SetInterfaceTheme(It.IsAny<InterfaceTheme>()), Times.Never);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InstallConfirmation_ConnectorShowsPublisherAndExecutionPermissions()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.connector","version":"1.2.3","displayName":"Example Connector","publisher":"Example Studio",
             "roles":{"connection":1},"entry":{"assembly":"Example.dll","type":"Example.Plugin"}}
            """).Manifest!;

        var window = PluginPackageConfirmationDialog.ForInstall(manifest, "example.nupkg");
        var layout = Assert.IsType<DialogLayout>(window.Content);

        Assert.Equal(DialogKind.Warning, layout.Kind);
        Assert.Contains("Example Connector", layout.Message);
        Assert.Contains("1.2.3", layout.Message);
        Assert.Contains("Example Studio", layout.Message);
        Assert.Contains("example.nupkg", layout.Message);
        Assert.Contains("same permissions as Invoklet", layout.Message);
        var buttons = Assert.IsType<StackPanel>(layout.Actions).Children.OfType<Button>();
        Assert.Equal("Cancel", Assert.Single(buttons, button => button.IsDefault).Content);
    }

    [AvaloniaFact]
    public void PluginsPage_BundledThemeToggle_ShowsDesiredStateAndKeepsTheCurrentSession()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.theme","version":"1.0.0","displayName":"Example theme","roles":{"theme":1}}
            """).Manifest!;
        var session = new PluginSession([new LoadedPlugin(manifest, "/plugins/theme", PluginTrust.Bundled, [])], []);
        var inventory = PluginInventory.Empty;
        var page = new PluginsPageViewModel(session, () => inventory, () => Task.CompletedTask,
            setEnabled: (id, enabled) => inventory = enabled ? PluginInventory.Empty : new PluginInventory(
                [new PluginInventoryEntry(id, manifest.Version, ["theme"], PluginInventoryStore.BundledSource, null,
                    new string('a', 64), "none", Enabled: false, Approved: false)]));
        var window = _fixture.CreateSut(page);

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            Assert.Contains("Content verified", PreferencesPluginsFixture.PageText(window));

            ClickToggle("Disable");
            Assert.Single(session.Loaded);
            Assert.True(page.HasPendingRestart);
            Assert.Contains("Will be disabled.", PreferencesPluginsFixture.PageText(window));

            ClickToggle("Enable");
            Assert.False(page.HasPendingRestart);
            Assert.Empty(inventory.Entries);
        }
        finally { window.Close(); }

        void ClickToggle(string label)
        {
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                item => item.IsEffectivelyVisible && Equals(item.Content, label));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PluginsPage_RejectedPlugin_ShowsSafeReasonAndMetadata()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.plugin","version":"1.0.0","displayName":"Example plugin","roles":{"theme":1}}
            """).Manifest!;
        var diagnostic = PluginDiagnostic.Error(manifest.Id, PluginDiagnosticCode.RegistrationFailed,
            "The plugin failed to initialize.", "Private path /home/person/plugins and token=secret-value");
        var session = new PluginSession([], [new RejectedPlugin(manifest.Id, manifest, "/home/person/plugins", PluginTrust.Trusted, [diagnostic])]);
        var window = _fixture.CreateSut(new PluginsPageViewModel(session, () => PluginInventory.Empty, () => Task.CompletedTask));

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);

            var text = PreferencesPluginsFixture.PageText(window);
            Assert.Contains("Example plugin", text);
            Assert.Contains("test.plugin", text);
            Assert.Contains("1.0.0 · theme · Trusted", text);
            Assert.Contains("Rejected", text);
            Assert.Contains("The plugin failed to initialize.", text);
            Assert.DoesNotContain(text, line => line?.Contains("/home/person", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(text, line => line?.Contains("secret-value", StringComparison.Ordinal) == true);
            Assert.False(window.FindControl<Button>("RestartForPluginsButton")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PluginsPage_StartupArgumentError_IsShownFirst()
    {
        var window = _fixture.CreateSut(new PluginsPageViewModel(PluginSession.Empty, () => PluginInventory.Empty, () => Task.CompletedTask,
            "--plugins-dev requires a directory."));

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);

            Assert.Contains("--plugins-dev requires a directory.", PreferencesPluginsFixture.PageText(window));
            Assert.False(window.FindControl<Button>("RestartForPluginsButton")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Search_BeforeThePageWasOpened_FindsAPluginByName()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.plugin","version":"1.0.0","displayName":"Example plugin","roles":{"theme":1}}
            """).Manifest!;
        var session = new PluginSession([new LoadedPlugin(manifest, "/plugins/test", PluginTrust.Trusted, [])], []);
        var window = _fixture.CreateSut(new PluginsPageViewModel(session, () => PluginInventory.Empty, () => Task.CompletedTask));

        try
        {
            window.Show();
            var viewModel = (PreferencesWindowViewModel)window.DataContext!;
            viewModel.FilterText = "Example plugin";
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(viewModel.Nodes, node => node.Id == "plugins");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PluginsPage_PluginFolderDisappears_RefreshShowsTheReasonWhileKeepingTheLoadedEntry()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.plugin","version":"1.0.0","displayName":"Example plugin","roles":{"theme":1}}
            """).Manifest!;
        var session = new PluginSession([new LoadedPlugin(manifest, "/plugins/test", PluginTrust.Trusted, [])], []);
        var inventory = new PluginInventory([new PluginInventoryEntry(manifest.Id, manifest.Version, ["theme"], "fixture", null,
            new string('a', 64), "none", Enabled: true, Approved: true)]);
        var exists = true;
        var window = _fixture.CreateSut(new PluginsPageViewModel(session, () => inventory, () => Task.CompletedTask,
            directoryExists: _ => exists));

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            Assert.DoesNotContain("The plugin folder is missing.", PreferencesPluginsFixture.PageText(window));
            exists = false;

            window.FindControl<Button>("RefreshPluginsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var text = PreferencesPluginsFixture.PageText(window);
            Assert.Contains("Loaded", text);
            Assert.Contains("Pending restart", text);
            Assert.Contains("The plugin folder is missing.", text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PluginsPage_InventoryChanges_ShowsPendingStateAndInvokesRestartAfterClosingPreferences()
    {
        var inventory = PluginInventory.Empty;
        var restart = new Mock<Func<Task>>();
        restart.Setup(run => run()).Returns(Task.CompletedTask);
        var window = _fixture.CreateSut(new PluginsPageViewModel(PluginSession.Empty, () => inventory, restart.Object));

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            var restartButton = window.FindControl<Button>("RestartForPluginsButton")!;
            Assert.False(restartButton.IsEffectivelyVisible);
            inventory = new PluginInventory([new PluginInventoryEntry("test.plugin", "2.0.0", ["theme"], "fixture", null,
                new string('a', 64), "none", Enabled: true, Approved: true)]);
            window.FindControl<Button>("RefreshPluginsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(restartButton.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Pending restart");

            restartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsVisible);
            restart.Verify(run => run(), Times.Once);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public async Task PluginsPage_InstallPackage_ChangesNothingUntilApprovedAndThenWaitsForRestart()
    {
        using var packages = new PluginPackageFolderFixture();
        var source = packages.WriteTheme(PluginPackageFolderFixture.Parent);
        var package = PluginPackageFolderFixture.Pack(source);
        var page = packages.CreatePage(packages.Load());
        var confirm = new Mock<Func<PluginManifest, Task<bool>>>();
        confirm.SetupSequence(ask => ask(It.IsAny<PluginManifest>())).ReturnsAsync(false).ReturnsAsync(true);
        var window = _fixture.CreateSut(page);

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            await page.InstallAsync(package, confirm.Object);
            Assert.Empty(packages.Store.Read().Entries);
            Assert.False(page.HasPendingRestart);

            await page.InstallAsync(package, confirm.Object);
            Dispatcher.UIThread.RunJobs();

            var text = PreferencesPluginsFixture.PageText(window);
            Assert.Contains("Pending restart", text);
            Assert.Contains("Will be enabled.", text);
            Assert.True(window.FindControl<Button>("RestartForPluginsButton")!.IsEffectivelyVisible);
            Assert.Equal(PluginContent.ComputeHash(source), Assert.Single(packages.Store.Read().Entries).ContentHash);
            confirm.Verify(ask => ask(It.Is<PluginManifest>(manifest => manifest.Id == PluginPackageFolderFixture.Parent)), Times.Exactly(2));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PluginsPage_CorruptedArchive_ShowsAnErrorWithoutAskingOrRevealingThePath()
    {
        using var packages = new PluginPackageFolderFixture();
        var archive = packages.WriteFile("broken.nupkg", "not an archive");
        var page = packages.CreatePage(PluginSession.Empty);
        var confirm = new Mock<Func<PluginManifest, Task<bool>>>();
        var window = _fixture.CreateSut(page);

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            await page.InstallAsync(archive, confirm.Object);
            Dispatcher.UIThread.RunJobs();

            var text = PreferencesPluginsFixture.PageText(window);
            Assert.Contains(text, line => line?.StartsWith("Could not update the plugin package.", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(text, line => line?.Contains(packages.Root, StringComparison.Ordinal) == true);
            Assert.Empty(packages.Store.Read().Entries);
            confirm.Verify(ask => ask(It.IsAny<PluginManifest>()), Times.Never);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PluginsPage_RemoveWithAnInstalledDependant_IsBlockedAndNamesTheDependant()
    {
        using var packages = new PluginPackageFolderFixture();
        packages.Install(PluginPackageFolderFixture.Parent);
        packages.Install(PluginPackageFolderFixture.Child, dependsOn: PluginPackageFolderFixture.Parent);
        var session = packages.Load();
        var page = packages.CreatePage(session);
        var confirm = new Mock<Func<Task<bool>>>();
        confirm.Setup(ask => ask()).ReturnsAsync(true);
        var window = _fixture.CreateSut(page);

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            Assert.Equal(2, session.Loaded.Count);
            await page.RemoveAsync(Row(PluginPackageFolderFixture.Parent), confirm.Object);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("Remove the plugins that depend on 'example.theme.ocean' first: Reef.", PreferencesPluginsFixture.PageText(window));
            Assert.Equal(2, packages.Store.Read().Entries.Count);
            Assert.False(page.HasPendingRestart);

            await page.RemoveAsync(Row(PluginPackageFolderFixture.Child), confirm.Object);
            await page.RemoveAsync(Row(PluginPackageFolderFixture.Parent), confirm.Object);

            Assert.Empty(packages.Store.Read().Entries);
            Assert.True(page.HasPendingRestart);
            Assert.Equal(2, session.Loaded.Count);
            confirm.Verify(ask => ask(), Times.Exactly(3));
        }
        finally { window.Close(); }

        PluginStatusRow Row(string id) => page.Rows.Single(row => row.Id == id && row.Status == "Loaded");
    }

    [AvaloniaFact]
    public async Task PluginsPage_ChangedPackageContents_ShowsContentChangedAndIsApprovedAgainByReinstalling()
    {
        using var packages = new PluginPackageFolderFixture();
        var source = packages.Install(PluginPackageFolderFixture.Parent);
        packages.ChangeInstalled(PluginPackageFolderFixture.Parent);
        var page = packages.CreatePage(packages.Load());
        var remove = new Mock<Func<Task<bool>>>();
        remove.Setup(ask => ask()).ReturnsAsync(true);
        var install = new Mock<Func<PluginManifest, Task<bool>>>();
        install.Setup(ask => ask(It.IsAny<PluginManifest>())).ReturnsAsync(true);
        var window = _fixture.CreateSut(page);

        try
        {
            PreferencesPluginsFixture.ShowPlugins(window);
            var text = PreferencesPluginsFixture.PageText(window);
            Assert.Contains("Content changed", text);
            Assert.Contains("Remove the plugin and install its package again to approve the new contents.", text);
            Assert.DoesNotContain(text, line => line?.Contains(packages.Root, StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Remove"));

            await page.RemoveAsync(page.Rows.Single(row => row.Status == "Content changed"), remove.Object);
            await page.InstallAsync(PluginPackageFolderFixture.Pack(source), install.Object);

            Assert.Equal(PluginContent.ComputeHash(source), Assert.Single(packages.Store.Read().Entries).ContentHash);
            Assert.Single(packages.Load().Loaded);
        }
        finally { window.Close(); }
    }
}

/// <summary>The Preferences window with the services its view model needs mocked; the Plugins page model is the test's own.</summary>
public sealed class PreferencesPluginsFixture
{
    public Mock<IThemeService> Theme { get; } = new();
    public Mock<IPreferencesService> Preferences { get; } = new();

    public PreferencesPluginsFixture()
    {
        var settings = new AppSettings();
        Theme.SetupGet(service => service.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false), new EditorTheme("Test", ThemeName.LightPlus, false)));
        Theme.SetupGet(service => service.InterfaceThemes).Returns([]);
        Theme.SetupGet(service => service.EditorThemes).Returns([]);
        Preferences.SetupGet(service => service.Keyboard).Returns(settings.Keyboard);
        Preferences.SetupGet(service => service.Experimental).Returns(settings.Experimental);
        Preferences.SetupGet(service => service.Editor).Returns(settings.Editor);
        Preferences.SetupGet(service => service.Session).Returns(settings.Session);
        Preferences.SetupGet(service => service.CSharp).Returns(settings.CSharp);
        Preferences.SetupGet(service => service.Logging).Returns(settings.Logging);
    }

    public PreferencesWindow CreateSut(PluginsPageViewModel plugins) =>
        new(new PreferencesWindowViewModel(Theme.Object, Preferences.Object, new LanguageHostRegistry(), pluginsPage: plugins));

    public static void ShowPlugins(PreferencesWindow window)
    {
        window.Show();
        var viewModel = (PreferencesWindowViewModel)window.DataContext!;
        viewModel.SelectedNode = viewModel.Nodes.Single(node => node.Id == "plugins");
        Dispatcher.UIThread.RunJobs();
    }

    public static string?[] PageText(PreferencesWindow window) =>
        window.FindControl<StackPanel>("PluginsSettingsPage")!.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
}

/// <summary>A temporary user plugin folder and inventory with theme packages written, installed and loaded through the real installer and loader.</summary>
public sealed class PluginPackageFolderFixture : IDisposable
{
    public const string Parent = "example.theme.ocean";
    public const string Child = "example.theme.reef";

    public string Root { get; } = Directory.CreateTempSubdirectory("Invoklet-plugins-page-").FullName;
    public PluginInventoryStore Store => new(Path.Combine(Root, PluginInventory.FileName));
    private string UserDirectory => Path.Combine(Root, "plugins");
    private PluginPackageInstaller Installer => new(UserDirectory, Store, BundledPluginCatalog.Empty);

    public string WriteTheme(string id, string? dependsOn = null)
    {
        var name = id == Parent ? "Ocean" : "Reef";
        var package = Directory.CreateDirectory(Path.Combine(Root, "source", id)).FullName;
        var dependency = dependsOn is null ? "" : $$""","dependsOn":["{{dependsOn}}"]""";
        File.WriteAllText(Path.Combine(package, PluginContracts.ManifestFileName), $$"""
            {"id":"{{id}}","version":"1.0.0","displayName":"{{name}}","roles":{"theme":1},"assets":{"themes":["theme.json"]}{{dependency}}}
            """);
        File.WriteAllText(Path.Combine(package, "theme.json"), $$"""
            {"id":"{{id}}","name":"{{name}}","extends":"{{dependsOn ?? "invoklet.theme.midnight"}}"}
            """);
        return package;
    }

    public string WriteFile(string name, string contents)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    public string Install(string id, string? dependsOn = null)
    {
        var source = WriteTheme(id, dependsOn);
        var installer = Installer;
        using var package = installer.PrepareArchive(Pack(source));
        installer.Install(package);
        return source;
    }

    /// <summary>Packs a package folder as a NuGet package with its files under <c>plugin/</c>, beside the folder.</summary>
    public static string Pack(string source)
    {
        var path = source + ".nupkg";
        File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(source))
            archive.CreateEntryFromFile(file, "plugin/" + Path.GetFileName(file));
        return path;
    }

    public void ChangeInstalled(string id) =>
        File.AppendAllText(Path.Combine(UserDirectory, id, "1.0.0", "theme.json"), " ");

    public PluginSession Load() => new PluginLoader(new PluginLoaderOptions
    {
        ApplicationVersion = new Version(1, 0),
        UserDirectory = UserDirectory,
        Inventory = Store.Read(),
        NativeLibraryCacheDirectory = Path.Combine(Root, "native"),
    }, [new ThemeCatalog()], NullLoggerFactory.Instance).Load();

    public PluginsPageViewModel CreatePage(PluginSession session) => new(session, Store.Read, () => Task.CompletedTask,
        setEnabled: (id, enabled) => Store.SetEnabled(id, enabled, BundledPluginCatalog.Empty, session: session, userDirectory: UserDirectory),
        createInstaller: () => Installer);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
