using Invoklet.UserControls;
using Invoklet.UserControls.Models;

namespace Invoklet.Services.Tabs;

/// <summary>What the file sync asks of the strip of tabs.</summary>
internal interface ITabDiskHost
{
    /// <summary>The strip went away.</summary>
    bool IsClosed { get; }

    IReadOnlyList<EditorTabItemViewModel> Tabs { get; }

    /// <summary>The tab being looked at.</summary>
    EditorTabItemViewModel? ActiveTab { get; }

    /// <summary>The tab is open and holds its text, so it can be reloaded; a tab whose text waits on disk reads its file when selected.</summary>
    bool HasContent(EditorTabItemViewModel tab);

    DiskReload Reload(EditorTabItemViewModel tab);

    /// <summary>The file is a script in another language: it opens in a new tab in this one's place.</summary>
    void Reopen(EditorTabItemViewModel tab);
}

/// <summary>How the state of a script's file is read; replaced in tests.</summary>
internal interface IScriptFileReader
{
    /// <summary>
    /// Reads and hashes the file away from the caller's thread: a script folder can be on a slow drive. A drive that does not
    /// answer in time is a <see cref="TimeoutException"/>; a cancelled read an <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ScriptFileState> ReadAsync(string path, CancellationToken cancellationToken);

    bool Exists(string path);
}

internal sealed class DiskScriptFileReader(TimeSpan? timeout = null) : IScriptFileReader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    // A read that has started cannot be interrupted, and on a network drive it can last long: past the timeout, or when the
    // check is cancelled, the caller stops waiting for it and the read ends on its own.
    public Task<ScriptFileState> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => ScriptFileState.Read(path), cancellationToken).WaitAsync(timeout ?? DefaultTimeout, cancellationToken);

    public bool Exists(string path) => File.Exists(path);
}

/// <summary>
/// Brings open tabs up to date with files another program changed: a tab without unsaved changes reloads
/// quietly, one with unsaved changes asks first. A tab that is running, saving or whose file is gone is left
/// alone; so is a version of the file the user already chose to keep their changes over.
/// </summary>
internal sealed class TabDiskSync(IWindowService windows, IScriptFileReader? reader = null)
{
    private readonly IScriptFileReader _reader = reader ?? new DiskScriptFileReader();
    private bool _checking;
    private bool _checkAgain;

    /// <summary>Runs when Invoklet's window is activated and when the scripts folder reports a change.</summary>
    internal async Task CheckAsync(ITabDiskHost host, CancellationToken cancellationToken = default)
    {
        if (_checking)
        {
            // A dialog of the running check is open: look again once it is done.
            _checkAgain = true;
            return;
        }

        _checking = true;
        try
        {
            do
            {
                _checkAgain = false;
                await CheckOnceAsync(host, cancellationToken);
            } while (_checkAgain && !host.IsClosed && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The strip went away: the files that were not read yet need not be.
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>A dialog yields to saves, runs and tab removal; its decision belongs to the old baseline.</summary>
    internal static bool CanReload(ITabDiskHost host, EditorTabItemViewModel tab, string path, ScriptFileState? known)
        => !host.IsClosed && host.HasContent(tab) && tab.FilePath == path
            && ReferenceEquals(tab.DiskState, known) && tab.RunState == RunState.Idle
            && tab.PendingSave is not { IsCompleted: false };

    /// <summary>File → Revert to Saved: the tab goes back to its file as it is on disk, asking first when that drops unsaved changes.</summary>
    internal async Task RevertAsync(ITabDiskHost host, EditorTabItemViewModel tab)
    {
        if (tab.FilePath is not { } path || !CanReload(host, tab, path, tab.DiskState)) return;
        var known = tab.DiskState;
        if (!_reader.Exists(path))
        {
            await windows.ShowErrorAsync("Revert Failed", $"'{Path.GetFileName(path)}' no longer exists on disk.");
            return;
        }

        if (tab.IsDirty && !await windows.ShowRevertFileDialogAsync(Path.GetFileName(path))) return;
        if (!ReferenceEquals(host.ActiveTab, tab) || !CanReload(host, tab, path, known)) return;
        await ReloadAsync(host, tab, "Revert Failed");
    }

    internal async Task ReloadAsync(ITabDiskHost host, EditorTabItemViewModel tab, string errorTitle)
    {
        switch (host.Reload(tab))
        {
            case DiskReload.OtherLanguage:
                host.Reopen(tab);
                break;
            case DiskReload.Unavailable:
                await windows.ShowErrorAsync(errorTitle, $"Could not read '{Path.GetFileName(tab.FilePath)}'.");
                break;
        }
    }

    private async Task CheckOnceAsync(ITabDiskHost host, CancellationToken cancellationToken)
    {
        var candidates = host.Tabs
            .Where(tab => host.HasContent(tab) && tab.FilePath is not null && tab.DiskState is not null)
            .Select(tab => (Tab: tab, Path: tab.FilePath!, Known: tab.DiskState!))
            .ToList();
        if (candidates.Count == 0) return;

        var states = new List<ScriptFileState?>(candidates.Count);
        foreach (var (_, path, _) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                states.Add(await _reader.ReadAsync(path, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                // Unreadable or not answering now: the next check looks again.
                states.Add(null);
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var (tab, path, known) = candidates[i];
            if (host.IsClosed) return;
            if (states[i] is not { Hash: not null } onDisk) continue;
            // Saved or reloaded meanwhile: what was read may be that very write, half done.
            if (!host.HasContent(tab) || tab.FilePath != path || !ReferenceEquals(tab.DiskState, known)) continue;
            if (known.SameAs(onDisk) || tab.DeclinedDiskState?.SameAs(onDisk) == true) continue;
            if (tab.RunState != RunState.Idle || tab.PendingSave is { IsCompleted: false }) continue;

            if (tab.IsDirty)
            {
                var reload = await windows.ShowReloadChangedFileDialogAsync(Path.GetFileName(path));
                if (!CanReload(host, tab, path, known)) continue;
                if (!reload)
                {
                    tab.DeclinedDiskState = onDisk;
                    continue;
                }
            }

            await ReloadAsync(host, tab, "Reload Failed");
        }
    }
}
