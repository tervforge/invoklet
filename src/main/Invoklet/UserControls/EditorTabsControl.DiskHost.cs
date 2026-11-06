using Invoklet.Services.Tabs;
using Invoklet.UserControls.Models;

namespace Invoklet.UserControls;

// What the file sync asks of the strip.
public partial class EditorTabsControl
{
    bool ITabDiskHost.IsClosed => _disposed;

    // A window's strip looks after its own tabs; the others' are their strips'.
    IReadOnlyList<EditorTabItemViewModel> ITabDiskHost.Tabs => _vm is null ? [] : [.. _vm.Tabs.Where(t => t.WindowId == _windowId)];

    bool ITabDiskHost.HasContent(EditorTabItemViewModel tab) => HasControl(tab);

    DiskReload ITabDiskHost.Reload(EditorTabItemViewModel tab)
        => _tabs.TryGetValue(tab, out var entry) ? entry.Control.ReloadFromDisk() : DiskReload.Unavailable;

    void ITabDiskHost.Reopen(EditorTabItemViewModel tab) => _vm?.ReopenTab(tab);
}
