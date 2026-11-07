using Invoklet.Format.MdRun;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Services.Tabs;
using Invoklet.UserControls;
using Invoklet.UserControls.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Services.Tabs.Fixtures;

internal sealed class TabDiskSyncFixture
{
    private readonly HashSet<EditorTabItemViewModel> _withoutContent = [];

    public TabDiskSyncFixture()
    {
        Windows.Setup(w => w.ShowReloadChangedFileDialogAsync(It.IsAny<string>())).ReturnsAsync(true);
        Windows.Setup(w => w.ShowRevertFileDialogAsync(It.IsAny<string>())).ReturnsAsync(true);
        Windows.Setup(w => w.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        Host.SetupGet(h => h.IsClosed).Returns(() => IsClosed);
        Host.SetupGet(h => h.Tabs).Returns(Tabs);
        Host.SetupGet(h => h.ActiveTab).Returns(() => ActiveTab);
        Host.Setup(h => h.HasContent(It.IsAny<EditorTabItemViewModel>())).Returns((EditorTabItemViewModel t) => !_withoutContent.Contains(t));
        Host.Setup(h => h.Reload(It.IsAny<EditorTabItemViewModel>())).Returns(DiskReload.Reloaded);
    }

    public Mock<IWindowService> Windows { get; } = new();

    public Mock<IScriptFileReader> Reader { get; } = new();

    public Mock<ITabDiskHost> Host { get; } = new();

    public List<EditorTabItemViewModel> Tabs { get; } = [];

    public bool IsClosed { get; set; }

    public EditorTabItemViewModel? ActiveTab { get; set; }

    /// <summary>How long a test waits for what should happen at once before it fails instead of hanging.</summary>
    public static TimeSpan Limit { get; } = TimeSpan.FromSeconds(5);

    public static ScriptFileState State(byte b) => new([b]);

    /// <summary>
    /// A tab on a file that held state 1 when the tab read it and holds <paramref name="onDisk"/> now; the file
    /// is there for <see cref="IScriptFileReader.Exists"/> unless <paramref name="exists"/> says otherwise.
    /// </summary>
    public EditorTabItemViewModel CreateTab(string name, byte onDisk = 2, bool dirty = false, bool exists = true)
    {
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Layout).Returns(new LayoutSettings());
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        var path = "/scripts/" + name + ".inv";
        var tab = new EditorTabItemViewModel(path, [], prefs.Object, new Mock<IEditorTabActions>().Object)
        {
            DiskState = State(1),
            IsDirty = dirty,
        };
        SetDiskState(path, onDisk);
        Reader.Setup(r => r.Exists(path)).Returns(exists);
        Tabs.Add(tab);
        return tab;
    }

    /// <summary>The tab no longer holds its text (closed, or its content is on disk).</summary>
    public void RemoveContent(EditorTabItemViewModel tab) => _withoutContent.Add(tab);

    public void SetDiskState(EditorTabItemViewModel tab, byte onDisk) => SetDiskState(tab.FilePath!, onDisk);

    private void SetDiskState(string path, byte onDisk) =>
        Reader.Setup(r => r.ReadAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(State(onDisk));

    /// <summary>Every read of a file goes to <paramref name="read"/>; a task that never completes is a drive that does not answer.</summary>
    public void ReadWith(Func<Task<ScriptFileState>> read) =>
        Reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(read);

    public TabDiskSync CreateSut() => new(Windows.Object, Reader.Object);
}
