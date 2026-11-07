using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.BuildCache;
using Invoklet.Services.Preferences;
using Invoklet.UserControls;
using Invoklet.UserControls.Models;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// Each tab asks its registered language for its own editor services, off the UI thread, and owns
// what it gets; a tab closed before they arrive cancels them and disposes whatever still comes back.
public sealed class EditorTabsEditorServicesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-editor-services").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // A registered language the test controls: each creation returns a new fake service, optionally
    // only once the test releases it.
    private class FakeLanguage(string id, bool hasEditorServices = true) : ILanguageHostFactory
    {
        public readonly List<Mock<IIntelliSenseService>> Created = [];
        public readonly List<CancellationToken> Tokens = [];
        public readonly List<bool> OnUiThread = [];
        public bool HonourCancellation { get; set; }
        public bool Throws { get; set; }
        public bool DisposeThrows { get; set; }

        // What the test waits for instead of polling: the first creation call has ended (with a service, or cancelled), and the first
        // service it made has been disposed.
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _lock = new();

        public string Language => id;
        public string DisplayName => id;
        public LanguageHostCapabilities Capabilities => LanguageHostCapabilities.IntelliSense;
        public IReadOnlyDictionary<string, string> Snippets { get; } = new Dictionary<string, string>();
        public EditorLanguageProfile EditorProfile { get; } = new();
        public ILanguageHost Create(ILanguageHostOptions? options = null) => throw new NotSupportedException();

        // Called through the contract's own CreateIntelliSenseAsync, which runs it off the UI thread.
        public IIntelliSenseService? CreateIntelliSense(CancellationToken cancellationToken)
        {
            Enter(cancellationToken);
            return Finish(cancellationToken);
        }

        protected void Enter(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Tokens.Add(cancellationToken);
                Started.TrySetResult();
                OnUiThread.Add(Dispatcher.UIThread.CheckAccess());
            }
        }

        protected IIntelliSenseService? Finish(CancellationToken cancellationToken)
        {
            try
            {
                if (HonourCancellation) cancellationToken.ThrowIfCancellationRequested();
                if (Throws) throw new InvalidOperationException("the language failed to start");
                if (!hasEditorServices) return null;
                var service = new Mock<IIntelliSenseService>();
                service.Setup(s => s.Dispose()).Callback(() =>
                {
                    FirstDisposed.TrySetResult();
                    if (DisposeThrows) throw new InvalidOperationException("dispose failed");
                });
                lock (_lock) Created.Add(service);
                return service.Object;
            }
            finally
            {
                Finished.TrySetResult();
            }
        }
    }

    // A language whose start-up takes until the test releases it: it waits for the gate without holding a thread.
    private sealed class GatedLanguage(string id, TaskCompletionSource gate) : FakeLanguage(id), ILanguageHostFactory
    {
        public async Task<IIntelliSenseService?> CreateIntelliSenseAsync(CancellationToken cancellationToken)
        {
            Enter(cancellationToken);
            await gate.Task;
            return Finish(cancellationToken);
        }
    }

    private (EditorTabsControl Tabs, MainWindowViewModel Shell, Window Host) Open(params ILanguageHostFactory[] languages) =>
        Open(null, languages);

    private (EditorTabsControl Tabs, MainWindowViewModel Shell, Window Host) Open(ILogger<EditorsTabItemControl>? logger, params ILanguageHostFactory[] languages)
    {
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Layout).Returns(new LayoutSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        prefs.Setup(p => p.Debugger).Returns(new DebuggerSettings());
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.Folder).Returns(_root);
        fileSystem.Setup(f => f.GetFilesTreeAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Dark, true),
            new EditorTheme("Test", TextMateSharp.Grammars.ThemeName.DarkPlus, true)));

        var registry = new LanguageHostRegistry();
        foreach (var language in languages) registry.Register(language);
        var shell = new MainWindowViewModel([], fileSystem.Object, new Mock<IWindowService>().Object, prefs.Object, new LanguageHostRegistry());
        var tabs = new EditorTabsControl(theme.Object, new Mock<IWindowService>().Object, registry, prefs.Object,
            new Mock<IBuildCacheService>().Object, logger);
        var host = new Window { Width = 900, Height = 700, Content = tabs };
        host.Show();
        return (tabs, shell, host);
    }

    private static async Task<EditorsTabItemControl> ShownAsync(EditorTabsControl tabs, EditorTabItemViewModel vm)
    {
        await TestWait.WhileAsync(() => tabs.ControlForTests(vm) is null);
        return tabs.ControlForTests(vm) ?? throw new InvalidOperationException("the tab never opened");
    }

    [AvaloniaFact]
    public async Task ARegisteredLanguage_Should_GiveEveryTabItsOwnEditorServices_OffTheUiThread()
    {
        var csharp = new FakeLanguage("csharp");
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.Attach(shell); // the initial blank C# tab
            shell.NewMdRunFile();

            var first = await ShownAsync(tabs, shell.Tabs[0]);
            var second = await ShownAsync(tabs, shell.Tabs[1]);

            Assert.Equal(2, csharp.Created.Count);
            // The two creations run side by side on the pool, so which service reaches which tab is not fixed: each tab has one of
            // them, and not the same one.
            Assert.NotSame(first.IntelliSenseForTests, second.IntelliSenseForTests);
            var created = csharp.Created.Select(c => c.Object).ToList();
            Assert.Contains(first.IntelliSenseForTests!, created);
            Assert.Contains(second.IntelliSenseForTests!, created);
            Assert.All(csharp.OnUiThread, Assert.False);
        }
        finally { host.Close(); }
    }

    // A language without editor services: the tab opens, with none, and nothing to dispose.
    [AvaloniaFact]
    public async Task ATabInALanguageWithoutEditorServices_Should_OpenWithNone()
    {
        var vb = new FakeLanguage("vb", hasEditorServices: false);
        var (tabs, shell, host) = Open(new FakeLanguage("csharp"), vb);
        try
        {
            tabs.Attach(shell);
            shell.NewMdRunFile(MdRunLanguage.Vb);

            var ctrl = await ShownAsync(tabs, shell.Tabs[^1]);

            Assert.Null(ctrl.IntelliSenseForTests);
            Assert.Single(vb.Tokens);
        }
        finally { host.Close(); }
    }

    // Closed while its services are still being created: the creation is cancelled, the tab is never
    // built, and a service that is returned anyway is disposed exactly once. A language that honours
    // the cancellation returns nothing to dispose.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingATab_WhileItsServicesAreCreated_Should_CancelThem_AndDisposeWhatArrivesOnce(bool honourCancellation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new GatedLanguage("csharp", gate) { HonourCancellation = honourCancellation };
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.Attach(shell);
            var vm = shell.Tabs[0];
            await csharp.Started.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);

            shell.Tabs.Remove(vm);
            Assert.True(csharp.Tokens[0].IsCancellationRequested);
            gate.TrySetResult();
            // Signals, not polling: the creation call has ended, and a service it made has been disposed (by whoever finds the tab gone).
            await csharp.Finished.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);
            if (!honourCancellation)
                await csharp.FirstDisposed.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);
            // Whatever the tab still had posted to the UI thread runs before the checks.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.Null(tabs.ControlForTests(vm));
            Assert.Equal(honourCancellation ? 0 : 1, csharp.Created.Count);
            foreach (var created in csharp.Created) created.Verify(s => s.Dispose(), Times.Once);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public async Task DisposingTheTabs_WhileServicesAreCreated_Should_DisposeWhatArrivesOnce()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new GatedLanguage("csharp", gate);
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.Attach(shell);
            await csharp.Started.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);

            tabs.Dispose();
            Assert.True(csharp.Tokens[0].IsCancellationRequested);
            gate.TrySetResult();
            await csharp.FirstDisposed.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.Null(tabs.ControlForTests(shell.Tabs[0]));
            csharp.Created[0].Verify(s => s.Dispose(), Times.Once);
        }
        finally { host.Close(); }
    }

    // An open tab owns its services: closing it disposes them once, and a second Dispose doesn't again.
    [AvaloniaFact]
    public async Task ClosingAnOpenTab_Should_DisposeItsServicesOnce()
    {
        var csharp = new FakeLanguage("csharp");
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.Attach(shell);
            var vm = shell.Tabs[0];
            var ctrl = await ShownAsync(tabs, vm);

            shell.Tabs.Remove(vm);
            ctrl.Dispose();

            csharp.Created[0].Verify(s => s.Dispose(), Times.Once);
        }
        finally { host.Close(); }
    }

    // A creation that never ends must not keep the tab (and the splash screen behind it) for ever: after the timeout the tab opens
    // without services, and the service that arrives later is disposed once.
    [AvaloniaFact]
    public async Task ACreationThatDoesNotEnd_Should_NotKeepTheTabForEver_AndTheLateServiceIsDisposed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new GatedLanguage("csharp", gate);
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.ServiceCreationTimeout = TimeSpan.FromMilliseconds(150);
            tabs.Attach(shell);

            var ctrl = await ShownAsync(tabs, shell.Tabs[0]);
            Assert.Null(ctrl.IntelliSenseForTests);

            gate.TrySetResult();
            await csharp.FirstDisposed.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);
            csharp.Created[0].Verify(s => s.Dispose(), Times.Once);
        }
        finally { host.Close(); }
    }

    // A language whose services cannot start: the tab opens as plain text, and the application stays up (the creation runs in an async
    // void handler, where an exception would end the process).
    [AvaloniaFact]
    public async Task ACreationThatFails_Should_OpenTheTabWithoutServices()
    {
        var csharp = new FakeLanguage("csharp") { Throws = true };
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.Attach(shell);

            var ctrl = await ShownAsync(tabs, shell.Tabs[0]);

            Assert.Null(ctrl.IntelliSenseForTests);
            Assert.Empty(csharp.Created);
        }
        finally { host.Close(); }
    }

    // A creation the tab stopped waiting for that fails later: nobody awaits it, so its failure is logged with the exception.
    [AvaloniaFact]
    public async Task ALateCreationThatFails_Should_BeLogged()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new GatedLanguage("csharp", gate) { Throws = true };
        var logged = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new Mock<ILogger<EditorsTabItemControl>>();
        logger.Setup(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((state, _) => (state.ToString() ?? string.Empty).Contains("arrived late")),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call => logged.TrySetResult((Exception?)call.Arguments[3])));
        var (tabs, shell, host) = Open(logger.Object, csharp);
        try
        {
            tabs.ServiceCreationTimeout = TimeSpan.Zero;
            tabs.Attach(shell);
            await ShownAsync(tabs, shell.Tabs[0]);

            gate.TrySetResult();
            var exception = await logged.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);

            Assert.Contains("the language failed to start", exception?.ToString());
        }
        finally { host.Close(); }
    }

    // What arrives late is disposed by nobody who could see a failure of it: the failure is logged, not left unobserved.
    [AvaloniaFact]
    public async Task ALateServiceThatFailsToDispose_Should_NotBreakTheTab()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new GatedLanguage("csharp", gate) { DisposeThrows = true };
        var (tabs, shell, host) = Open(csharp);
        try
        {
            tabs.ServiceCreationTimeout = TimeSpan.FromMilliseconds(150);
            tabs.Attach(shell);
            var ctrl = await ShownAsync(tabs, shell.Tabs[0]);

            gate.TrySetResult();
            await csharp.FirstDisposed.Task.WaitAsync(TestWait.DefaultTimeout, TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.NotNull(tabs.ControlForTests(shell.Tabs[0]));
            Assert.Same(ctrl, tabs.ControlForTests(shell.Tabs[0]));
        }
        finally { host.Close(); }
    }
}
