using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Languages.CSharp;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Windows;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

// Every modal window closes on Escape, as a dialog does everywhere. Windows that ask before dropping unsaved edits (Secret Store,
// Connections, Dependencies) and the ones whose Escape does something first (Quick Open, a breakpoint's condition) have tests of their own.
public class EscapeClosesDialogsTests
{
    public static TheoryData<string> Dialogs => new(
        "error", "newfile", "goto", "reload", "revert", "exit", "conflict", "unsaved", "rename", "connection", "secret", "about",
        "preferences", "nuget", "setup");

    private static Window Create(string kind) => kind switch
    {
        "error" => new ErrorDialog("Title", "A message"),
        "newfile" => new NewFileKindDialog(),
        "goto" => new GoToLineDialog(1, 10),
        "reload" => ReloadFileDialog.ChangedOnDisk("a.inv"),
        "revert" => ReloadFileDialog.Revert("a.inv"),
        "exit" => new ExitUnsavedDialog(["a.inv", "b.inv"]),
        "conflict" => new SaveConflictDialog("a.inv"),
        "unsaved" => new UnsavedChangesDialog("a.inv"),
        "rename" => new RenameSymbolDialog("Name"),
        "connection" => new ConnectionPackageConfirmationDialog("Add it?"),
        "secret" => new SecretAccessDialog("report.inv", "billing", isSave: false),
        "preferences" => new PreferencesWindow(PreferencesViewModel()),
        "nuget" => new NuGetSourcesWindow(new NuGetSourcesWindowViewModel(NuGetSources())),
        "setup" => new SetupInstallConfirmationDialog(new Invoklet.Services.Setup.SetupInstallPlan(".NET SDK", "10.0.403", "/home/me/.dotnet", "Microsoft", false)),
        _ => new AboutWindow(),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public void Escape_Should_CloseTheModalWindow(string kind)
    {
        var window = Create(kind);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(closed, $"{window.GetType().Name} stayed open after Escape");
        }
        finally
        {
            if (!closed) window.Close();
        }
    }

    private static PreferencesWindowViewModel PreferencesViewModel()
    {
        var registry = new LanguageHostRegistry();
        registry.Register(new CSharpLanguageHostFactory(NullLoggerFactory.Instance));
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false),
            new EditorTheme("Test", ThemeName.LightPlus, false)));
        theme.Setup(t => t.InterfaceThemes).Returns([]);
        theme.Setup(t => t.EditorThemes).Returns([]);
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Experimental).Returns(new ExperimentalSettings());
        return new PreferencesWindowViewModel(theme.Object, prefs.Object, registry);
    }

    private static INuGetSourceStore NuGetSources()
    {
        var store = new Mock<INuGetSourceStore>();
        store.Setup(s => s.Load()).Returns([new NuGetSource("nuget.org", "https://api.nuget.org/v3/index.json")]);
        return store.Object;
    }
}
