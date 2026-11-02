using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
using Invoklet.Windows;
using Invoklet.Windows.Healpers;
using Invoklet.Windows.Models;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

// Quitting the app used to skip the unsaved-changes
// confirmation entirely (only closing an individual tab asked — see
// EditorTabsControlCloseConfirmationTests) — MainWindow.OnClosing now cancels the first Closing,
// awaits EditorTabsControl.ConfirmCloseAllUnsavedTabsAsync, and only actually closes once that
// resolves true. These drive a real MainWindow through Close() end to end rather than calling the
// confirmation methods directly.
public class MainWindowClosingTests
{
    private static (MainWindow Window, MainWindowViewModel ViewModel, Mock<IWindowService> WindowService)
        CreateMainWindow()
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
        var buildCache = new Mock<IBuildCacheService>();
        var intelliSense = new Mock<IIntelliSenseService>();
        var windowService = new Mock<IWindowService>();

        var mainVm = new MainWindowViewModel(
            sdkProviders: [],
            fileSystemService: fileSystem.Object,
            windowService: windowService.Object,
            prefs: prefs.Object,
            languageRegistry: new LanguageHostRegistry());

        var editorTabs = new EditorTabsControl(
            theme.Object, new Mock<IWindowService>().Object, registry.WithCSharpIntelliSense(() => intelliSense.Object), prefs.Object,
            buildCache.Object);

        var window = new MainWindow(prefs.Object, editorTabs, mainVm);
        window.Show();
        return (window, mainVm, windowService);
    }

    private static EditorsTabItemControl? SoleControl(MainWindow window) => window.GetVisualDescendants().OfType<TabItem>()
        .Select(t => t.Content).OfType<EditorsTabItemControl>().FirstOrDefault();

    private static async Task<EditorsTabItemControl> GetSoleControlAsync(MainWindow window)
    {
        await TestWait.WhileAsync(() => SoleControl(window) is null);
        return SoleControl(window)!;
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WithNothingUnsaved_Should_CloseImmediately_WithoutAskingAnything()
    {
        var (window, _, windowService) = CreateMainWindow();
        await GetSoleControlAsync(window); // let the initial (pristine, untitled) tab realize first

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        await window.ClosingTask;

        Assert.True(closedFired);
        windowService.Verify(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()), Times.Never);
    }

    // The output of a run that has just ended is written in the background: closing waits for it, so that the last result is not lost, and
    // the window is not frozen while it waits.
    [AvaloniaFact]
    public async Task ClosingTheWindow_WhileTheOutputOfTheLastRunIsBeingWritten_Should_WaitForItAndThenClose()
    {
        var (window, _, _) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        using var gate = new ManualResetEventSlim(false);
        var written = ctrl.QueueOutputJobForTests(() => gate.Wait(TimeSpan.FromSeconds(30)));
        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        var closing = window.ClosingTask;

        Assert.False(closing.IsCompleted); // waiting for the write
        Assert.False(closedFired);
        var dispatched = false;
        await Dispatcher.UIThread.InvokeAsync(() => dispatched = true);
        Assert.True(dispatched);

        gate.Set();
        await written;
        await closing;

        Assert.True(closedFired);
    }

    // The confirmations come first, and a Save As made by one of them writes the output of its tab again: the last wait is after them, just before the
    // close, and covers that write too.
    [AvaloniaFact]
    public async Task ClosingTheWindow_WhenAConfirmationQueuesAnOutputWrite_Should_WaitForThatWriteToo()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        using var gate = new ManualResetEventSlim(false);
        Task<bool>? written = null;
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>())).ReturnsAsync(() =>
        {
            written = ctrl.QueueOutputJobForTests(() => gate.Wait(TimeSpan.FromSeconds(30)));
            return UnsavedChangesResult.DontSave;
        });
        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        var closing = window.ClosingTask;
        await TestWait.WhileAsync(() => written is null); // the confirmation has been answered

        Assert.False(closing.IsCompleted);
        Assert.False(closedFired); // the answer came, and the write it made is still being waited for

        gate.Set();
        await written!;
        await closing;

        Assert.True(closedFired);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WhileTheOutputIsNotWrittenInTime_Should_GiveItUpAndClose()
    {
        var (window, _, _) = CreateMainWindow();
        window.OutputFlushLimit = TimeSpan.FromMilliseconds(100);
        var ctrl = await GetSoleControlAsync(window);
        using var gate = new ManualResetEventSlim(false);
        _ = ctrl.QueueOutputJobForTests(() => gate.Wait(TimeSpan.FromSeconds(30)));
        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        try
        {
            window.Close();
            await window.ClosingTask;

            Assert.True(closedFired);
        }
        finally { gate.Set(); }
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WithADirtyUntitledTab_AndCancelling_Should_KeepTheWindowOpen()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()))
            .ReturnsAsync(UnsavedChangesResult.Cancel);

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        await window.ClosingTask;

        Assert.False(closedFired);
        windowService.Verify(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()), Times.Once);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WithADirtyUntitledTab_AndChoosingDontSave_Should_ActuallyClose()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()))
            .ReturnsAsync(UnsavedChangesResult.DontSave);

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        await window.ClosingTask;

        // The confirmed, second Close() has to make it all the way through the (now no-op) Closing
        // handler again — this is what actually proves the _confirmedClose re-entry guard works,
        // not just that ConfirmCloseAllUnsavedTabsAsync itself returns true in isolation.
        Assert.True(closedFired);
    }

    // A second Close (window button / Cmd+Q again) while the first confirmation dialog
    // is still open must not start a second, overlapping walk over the dirty tabs.
    [AvaloniaFact]
    public async Task ClosingTheWindowAgain_WhileTheConfirmationIsPending_Should_NotAskTwice()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        var answer = new TaskCompletionSource<UnsavedChangesResult>();
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>())).Returns(answer.Task);

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        window.Close();
        var closing = window.ClosingTask;
        window.Close();

        Assert.False(closing.IsCompleted);
        Assert.False(closedFired);
        windowService.Verify(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()), Times.Once);

        answer.SetResult(UnsavedChangesResult.DontSave);
        await closing;

        Assert.True(closedFired);
    }

    // A text editor window with unsaved changes is asked about by the main window as it closes —
    // once, after the tabs — instead of cancelling the close on its own and leaving the app running.
    private static async Task<TextEditorWindow> OpenDirtyTextEditorAsync(
        MainWindow owner, Mock<IWindowService> textWindowService, UnsavedChangesResult answer)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("Invoklet-exit").FullName, "notes.txt");
        await File.WriteAllTextAsync(path, "old");
        textWindowService.Setup(s => s.ShowUnsavedChangesDialogAsync("notes.txt")).ReturnsAsync(answer);
        var viewModel = await TextEditorWindowViewModel.LoadAsync(path, textWindowService.Object);
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Dark, true),
            new EditorTheme("Test", TextMateSharp.Grammars.ThemeName.DarkPlus, true)));
        var editor = new TextEditorWindow(viewModel, theme.Object, preferences: null);
        editor.Show(owner);
        editor.Editor.Document.Insert(0, "new ");
        Assert.True(viewModel.IsDirty);
        return editor;
    }

    [AvaloniaTheory]
    [InlineData(UnsavedChangesResult.Cancel, false)]
    [InlineData(UnsavedChangesResult.DontSave, true)]
    public async Task FileExit_WithADirtyTextEditorWindow_Should_AskOnce_ThenCloseBothOrNeither(
        UnsavedChangesResult answer, bool closes)
    {
        var (window, _, _) = CreateMainWindow();
        await GetSoleControlAsync(window);
        var textWindowService = new Mock<IWindowService>();
        var editor = await OpenDirtyTextEditorAsync(window, textWindowService, answer);

        var mainClosed = false;
        var editorClosed = false;
        window.Closed += (_, _) => mainClosed = true;
        editor.Closed += (_, _) => editorClosed = true;

        ExecuteFileExit(window);
        await window.ClosingTask;

        Assert.Equal(closes, mainClosed);
        Assert.Equal(closes, editorClosed);
        textWindowService.Verify(s => s.ShowUnsavedChangesDialogAsync("notes.txt"), Times.Once);
    }

    // File → Exit, on every OS (macOS included, with Cmd+Q): the same Close() and confirmation as
    // the window's close button.
    private static void ExecuteFileExit(MainWindow window)
    {
        var file = window.MainMenuEntries.Single(g => g.Header == "File");
        file.Items.OfType<MainMenuCommand>().Single(c => c.Key == KeyCommands.Exit).Command.Execute(null);
    }

    [AvaloniaFact]
    public async Task FileExit_WithADirtyUntitledTab_AndCancelling_Should_KeepTheWindowOpen()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()))
            .ReturnsAsync(UnsavedChangesResult.Cancel);

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        ExecuteFileExit(window);
        await window.ClosingTask;

        Assert.False(closedFired);
        Assert.True(window.IsVisible);
        windowService.Verify(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()), Times.Once);
    }

    [AvaloniaFact]
    public async Task FileExit_WithADirtyUntitledTab_AndChoosingDontSave_Should_Close()
    {
        var (window, _, windowService) = CreateMainWindow();
        var ctrl = await GetSoleControlAsync(window);
        ctrl.State.Editor.Text = "Console.WriteLine(1);";
        windowService.Setup(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()))
            .ReturnsAsync(UnsavedChangesResult.DontSave);

        var closedFired = false;
        window.Closed += (_, _) => closedFired = true;

        ExecuteFileExit(window);
        await window.ClosingTask;

        Assert.True(closedFired);
        windowService.Verify(w => w.ShowUnsavedChangesDialogAsync(It.IsAny<string>()), Times.Once);
    }
}
