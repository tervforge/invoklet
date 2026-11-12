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
using Moq;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

public sealed class PreferencesPluginsTests
{
    private readonly PreferencesPluginsFixture _fixture = new();

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
