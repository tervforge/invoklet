using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.BuildCache;
using Invoklet.Services.Preferences;
using Invoklet.UserControls;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;

// The tab strip in a window of a given width, over a main window's view model with new, unsaved tabs; the window, the strip and the view
// model go when the fixture does.
internal sealed class EditorTabsControlStripFixture : IAsyncDisposable
{
    private Window? _window;

    public EditorTabsControl? Strip { get; private set; }
    public MainWindowViewModel? ViewModel { get; private set; }

    public async Task<EditorTabsControl> CreateSutAsync(int tabs, double width = 600)
    {
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Layout).Returns(new LayoutSettings());
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.Folder).Returns(Path.GetTempPath());
        fileSystem.Setup(f => f.GetFilesTreeAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Dark, true),
            new EditorTheme("Test", TextMateSharp.Grammars.ThemeName.DarkPlus, true)));
        var intelliSense = new Mock<IIntelliSenseService>();

        ViewModel = new MainWindowViewModel(sdkProviders: [], fileSystemService: fileSystem.Object,
            windowService: new Mock<IWindowService>().Object, prefs: prefs.Object, languageRegistry: new LanguageHostRegistry());
        // A window opened for a tab gets a strip of its own from the services, as in the app.
        var services = new Mock<IServiceProvider>();
        EditorTabsControl NewStrip() => new(theme.Object, new Mock<IWindowService>().Object,
            new Mock<ILanguageHostRegistry>().WithCSharpIntelliSense(() => intelliSense.Object), prefs.Object, new Mock<IBuildCacheService>().Object,
            services: services.Object);
        services.Setup(s => s.GetService(typeof(EditorTabsControl))).Returns(() =>
        {
            var strip = NewStrip();
            _otherStrips.Add(strip);
            return strip;
        });
        Strip = NewStrip();
        _window = new Window { Width = width, Height = 300, Content = Strip };
        // The theme's backgrounds: without one a header has no surface, and a press goes through it.
        _window.Resources["Invoklet.Panel.BackgroundBrush"] = Brushes.DimGray;
        _window.Resources["Invoklet.Editor.BackgroundBrush"] = Brushes.Black;
        _window.Show();
        Strip.Attach(ViewModel); // opens the first, blank tab
        for (var i = 1; i < tabs; i++) ViewModel.NewMdRunFile();
        await Strip.TabsReadyForTests();
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
        return Strip;
    }

    private readonly List<EditorTabsControl> _otherStrips = [];

    public Window Window => _window!;

    /// <summary>The window a strip opened for a tab lies in, or null once it closed.</summary>
    public static Window? WindowOf(EditorTabsControl strip) => TopLevel.GetTopLevel(strip) as Window;

    /// <summary>The row of a strip's headers and buttons.</summary>
    public static Grid StripRowOf(EditorTabsControl strip) =>
        strip.FindControl<TabControl>("FileTabControl")!.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_StripRow");

    /// <summary>The window's overlay layer, where a carried tab's picture and the drop mark lie.</summary>
    public global::Avalonia.Controls.Primitives.OverlayLayer Overlay => global::Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(Strip!)!;

    public TabControl Tabs => Strip!.FindControl<TabControl>("FileTabControl")!;

    public IReadOnlyList<TabItem> Headers => [.. Tabs.Items.OfType<TabItem>()];

    public T Part<T>(string name) where T : Control => Tabs.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>Where <paramref name="control"/> lies in the window.</summary>
    public Rect InWindow(Control control) => new(control.TranslatePoint(new Point(0, 0), Window)!.Value, control.Bounds.Size);

    public async ValueTask DisposeAsync()
    {
        foreach (var strip in _otherStrips) WindowOf(strip)?.Close();
        _window?.Close();
        Strip?.Dispose();
        ViewModel?.Dispose();
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }
}
