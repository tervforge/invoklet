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
    [AvaloniaFact]
    public void PluginsPage_RejectedPlugin_ShowsSafeReasonAndMetadata()
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"test.plugin","version":"1.0.0","displayName":"Example plugin","roles":{"theme":1}}
            """).Manifest!;
        var diagnostic = PluginDiagnostic.Error(manifest.Id, PluginDiagnosticCode.RegistrationFailed,
            "The plugin failed to initialize.", "Private path /home/person/plugins and token=secret-value");
        var session = new PluginSession([], [new RejectedPlugin(manifest.Id, manifest, "/home/person/plugins", PluginTrust.Trusted, [diagnostic])]);
        var window = CreateWindow(new PluginsPageViewModel(session, () => PluginInventory.Empty, () => Task.CompletedTask));

        try
        {
            ShowPlugins(window);

            var text = window.FindControl<StackPanel>("PluginsSettingsPage")!.GetVisualDescendants()
                .OfType<TextBlock>().Select(block => block.Text).ToArray();
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
    public void PluginsPage_InventoryChanges_ShowsPendingStateAndInvokesRestartAfterClosingPreferences()
    {
        var inventory = PluginInventory.Empty;
        var restart = new Mock<Func<Task>>();
        restart.Setup(run => run()).Returns(Task.CompletedTask);
        var window = CreateWindow(new PluginsPageViewModel(PluginSession.Empty, () => inventory, restart.Object));

        try
        {
            ShowPlugins(window);
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

    private static PreferencesWindow CreateWindow(PluginsPageViewModel plugins)
    {
        var settings = new AppSettings();
        var theme = new Mock<IThemeService>();
        theme.SetupGet(service => service.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false), new EditorTheme("Test", ThemeName.LightPlus, false)));
        theme.SetupGet(service => service.InterfaceThemes).Returns([]);
        theme.SetupGet(service => service.EditorThemes).Returns([]);
        var prefs = new Mock<IPreferencesService>();
        prefs.SetupGet(service => service.Keyboard).Returns(settings.Keyboard);
        prefs.SetupGet(service => service.Experimental).Returns(settings.Experimental);
        prefs.SetupGet(service => service.Editor).Returns(settings.Editor);
        prefs.SetupGet(service => service.Session).Returns(settings.Session);
        prefs.SetupGet(service => service.CSharp).Returns(settings.CSharp);
        prefs.SetupGet(service => service.Logging).Returns(settings.Logging);
        return new PreferencesWindow(new PreferencesWindowViewModel(theme.Object, prefs.Object, new LanguageHostRegistry(), pluginsPage: plugins));
    }

    private static void ShowPlugins(PreferencesWindow window)
    {
        window.Show();
        var viewModel = (PreferencesWindowViewModel)window.DataContext!;
        viewModel.SelectedNode = viewModel.Nodes.Single(node => node.Id == "plugins");
        Dispatcher.UIThread.RunJobs();
    }
}
