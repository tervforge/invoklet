using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.BuildCache;
using Invoklet.Services.Preferences;
using Invoklet.UserControls;
using Invoklet.Windows;
using Invoklet.Windows.Healpers;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Services;

// "Restart now" goes through the main window's own close, which asks about unsaved tabs: a cancelled close leaves the window
// and starts nothing, a confirmed one lets the exit handler start one replacement with the original arguments.
public sealed class ApplicationRestartTests
{
    private readonly ApplicationRestartFixture _fixture = new();

    [AvaloniaTheory]
    [InlineData(UnsavedChangesResult.Cancel, false)]
    [InlineData(UnsavedChangesResult.DontSave, true)]
    public async Task RequestAsync_WithADirtyTab_LaunchesOnlyAfterConfirmedExit(UnsavedChangesResult answer, bool exits)
    {
        var window = _fixture.ShowMainWindow();
        var tab = await _fixture.GetSoleTabAsync(window);
        tab.State.Editor.Text = "Console.WriteLine(1);";
        _fixture.WindowService.Setup(service => service.ShowUnsavedChangesDialogAsync(It.IsAny<string>())).ReturnsAsync(answer);
        var restart = _fixture.CreateSut(window);

        await restart.RequestAsync();
        Assert.Empty(_fixture.Launches);
        Assert.Equal(!exits, window.IsVisible);
        if (window.IsVisible)
        {
            _fixture.WindowService.Setup(service => service.ShowUnsavedChangesDialogAsync(It.IsAny<string>()))
                .ReturnsAsync(UnsavedChangesResult.DontSave);
            window.Close();
            await window.ClosingTask;
        }
        restart.StartReplacementAfterExit();
        restart.StartReplacementAfterExit();

        if (exits)
            Assert.Equal(ApplicationRestartFixture.Arguments, Assert.Single(_fixture.Launches).ArgumentList.TakeLast(2));
        else
            Assert.Empty(_fixture.Launches);
    }
}

/// <summary>A real main window with one tab, a desktop lifetime that reports it, and a recorded process launch.</summary>
internal sealed class ApplicationRestartFixture
{
    public static readonly string[] Arguments = ["--plugins-dev", "example path"];

    public Mock<IWindowService> WindowService { get; } = new();
    public Mock<IClassicDesktopStyleApplicationLifetime> Desktop { get; } = new();
    public List<ProcessStartInfo> Launches { get; } = [];

    internal ApplicationRestart CreateSut(MainWindow mainWindow)
    {
        Desktop.SetupGet(lifetime => lifetime.MainWindow).Returns(mainWindow);
        Desktop.SetupGet(lifetime => lifetime.Args).Returns(Arguments);
        return new ApplicationRestart(Desktop.Object, NullLogger<ApplicationRestart>.Instance, Launches.Add);
    }

    public MainWindow ShowMainWindow()
    {
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Layout).Returns(new LayoutSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());

        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.Folder).Returns(Path.GetTempPath());
        fileSystem.Setup(f => f.GetFilesTreeAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Dark, true),
            new EditorTheme("Test", TextMateSharp.Grammars.ThemeName.DarkPlus, true)));

        var registry = new Mock<ILanguageHostRegistry>();
        var intelliSense = new Mock<IIntelliSenseService>();
        var mainVm = new MainWindowViewModel(
            sdkProviders: [],
            fileSystemService: fileSystem.Object,
            windowService: WindowService.Object,
            prefs: prefs.Object,
            languageRegistry: new LanguageHostRegistry());
        var editorTabs = new EditorTabsControl(
            theme.Object, new Mock<IWindowService>().Object, registry.WithCSharpIntelliSense(() => intelliSense.Object), prefs.Object,
            new Mock<IBuildCacheService>().Object);

        var window = new MainWindow(prefs.Object, editorTabs, mainVm);
        window.Show();
        return window;
    }

    public async Task<EditorsTabItemControl> GetSoleTabAsync(MainWindow window)
    {
        await TestWait.WhileAsync(() => SoleTab(window) is null);
        return SoleTab(window)!;
    }

    private static EditorsTabItemControl? SoleTab(MainWindow window) => window.GetVisualDescendants().OfType<TabItem>()
        .Select(t => t.Content).OfType<EditorsTabItemControl>().FirstOrDefault();
}
