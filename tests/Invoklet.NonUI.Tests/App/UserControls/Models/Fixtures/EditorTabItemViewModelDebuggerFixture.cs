using Invoklet.Format.MdRun;
using Invoklet.Services.Preferences;
using Invoklet.UserControls.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.UserControls.Models.Fixtures;

// A tab whose debugger is there or not as Installed says; Preferences changing (a debugger set or installed) is RaisePreferencesChanged.
internal sealed class EditorTabItemViewModelDebuggerFixture
{
    public Mock<IPreferencesService> Preferences { get; } = new();

    public Mock<IEditorTabActions> Actions { get; } = new();

    public bool Installed { get; set; }

    public EditorTabItemViewModelDebuggerFixture()
    {
        Preferences.Setup(p => p.Layout).Returns(new LayoutSettings());
        Preferences.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        Preferences.Setup(p => p.Editor).Returns(new EditorSettings());
        Preferences.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        Preferences.Setup(p => p.Session).Returns(new SessionSettings());
    }

    public void RaisePreferencesChanged() => Preferences.Raise(p => p.Changed += null);

    public EditorTabItemViewModel CreateSut() =>
        new("/scripts/a.inv", [], Preferences.Object, Actions.Object, debuggerInstalled: () => Installed);
}
