using Invoklet.Contracts;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// A main window's view model with three new, unsaved tabs (nothing on the disk), the window service and the file tree faked.
internal sealed class MainWindowViewModelMoveTabFixture : IDisposable
{
    private MainWindowViewModel? _created;

    public Mock<IPreferencesService> Preferences { get; } = new();

    public MainWindowViewModel CreateSut()
    {
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.Folder).Returns(Path.GetTempPath());
        fileSystem.Setup(f => f.GetFilesTreeAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Preferences.Setup(p => p.Layout).Returns(new LayoutSettings());
        Preferences.Setup(p => p.Session).Returns(new SessionSettings());
        Preferences.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        Preferences.Setup(p => p.Editor).Returns(new EditorSettings());
        Preferences.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        _created = new MainWindowViewModel(sdkProviders: [], fileSystemService: fileSystem.Object,
            windowService: new Mock<IWindowService>().Object, prefs: Preferences.Object, languageRegistry: new LanguageHostRegistry());
        for (var i = 0; i < 3; i++) _created.NewMdRunFile();
        return _created;
    }

    public void Dispose() => _created?.Dispose();
}
