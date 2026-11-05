using Avalonia.Threading;
using Invoklet.Contracts;
using Invoklet.Services.Preferences;

namespace Invoklet.Services.Secrets;

/// <summary>
/// Whether a running script may use the Secret Store: the first time a script asks, the user is asked. Always Allow is kept in the
/// settings by the script's full path; Allow Once and a script not saved yet (one answer for all of them) last until Invoklet closes.
/// A no is not remembered: the next run asks again.
/// </summary>
public sealed class SecretAccessGate(IPreferencesService preferences, Func<string, string, bool, CancellationToken, Task<SecretAccessChoice>> ask)
{
    // Paths are compared as the file system does: letter case aside on Windows and macOS.
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private const string Unsaved = "";

    private readonly HashSet<string> _allowedThisSession = new(PathComparer);

    // One question at a time: a script asking for three secrets, or two scripts at once, is asked once per script.
    private readonly SemaphoreSlim _asking = new(1, 1);

    /// <summary>
    /// <see cref="MayAccessAsync"/> on the UI thread, from the thread a script's request arrives on: the question is a dialog, and saving
    /// the settings tells the windows, which update themselves on the thread that owns them.
    /// </summary>
    public Task<bool> MayAccessOnUiThreadAsync(SecretAccess access, CancellationToken cancellationToken)
        => Dispatcher.UIThread.InvokeAsync(() => MayAccessAsync(access, cancellationToken));

    public async Task<bool> MayAccessAsync(SecretAccess access, CancellationToken cancellationToken)
    {
        var script = access.ScriptPath is { Length: > 0 } path ? Path.GetFullPath(path) : Unsaved;
        if (IsAllowed(script)) return true;
        await _asking.WaitAsync(cancellationToken);
        try
        {
            if (IsAllowed(script)) return true;
            var name = script == Unsaved ? "an unsaved script" : Path.GetFileName(script);
            var choice = await ask(name, access.Name, access.IsSave, cancellationToken);
            if (choice == SecretAccessChoice.Deny || cancellationToken.IsCancellationRequested) return false;
            // Always is kept only in the settings, so taking it away there (the Secret Store window) works at once.
            if (choice == SecretAccessChoice.Always && script != Unsaved)
            {
                preferences.Secrets.AllowedScripts.Add(script);
                preferences.Save();
            }
            else
            {
                lock (_allowedThisSession) _allowedThisSession.Add(script);
            }
            return true;
        }
        finally
        {
            _asking.Release();
        }
    }

    private bool IsAllowed(string script)
    {
        lock (_allowedThisSession)
            if (_allowedThisSession.Contains(script)) return true;
        return script != Unsaved && preferences.Secrets?.AllowedScripts.Contains(script, PathComparer) == true;
    }
}
