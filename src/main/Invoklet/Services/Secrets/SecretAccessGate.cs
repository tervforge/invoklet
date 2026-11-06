using System.Security.Cryptography;
using Avalonia.Threading;
using Invoklet.Contracts;
using Invoklet.Services.Preferences;

namespace Invoklet.Services.Secrets;

/// <summary>
/// Whether a running script may use the Secret Store: the first time a script asks, the user is asked. Always Allow is kept in the
/// settings by the script's full path and the contents of its file, which Invoklet's own saves keep up to date: a file another program
/// changed is asked about again. Read access and read/write access are kept separately; creating a secret or approving one replacement
/// grants write access to that version of the script. Allow Once and a script not saved yet last until Invoklet closes. A no is not
/// remembered: the next run asks again.
/// </summary>
/// <param name="confirmChange">Whether an allowed script may replace (or, <c>true</c>, remove) a secret already kept: script name, secret,
/// removes. Without it, an allowed script changes secrets unasked.</param>
/// <param name="fingerprint">The SHA-256 (hex) of a file, or null when it can't be read; by default read from the disk.</param>
public sealed class SecretAccessGate(IPreferencesService preferences, Func<string, string, bool, CancellationToken, Task<SecretAccessChoice>> ask,
    Func<string, string?>? fingerprint = null, Func<string, string, bool, CancellationToken, Task<bool>>? confirmChange = null)
{
    private readonly Func<string, string?> _fingerprint = fingerprint ?? FileFingerprint;

    // Paths are compared as the file system does: letter case aside on Windows and macOS.
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private const string Unsaved = "";

    private readonly HashSet<string> _allowedThisSession = new(PathComparer);
    private readonly Dictionary<string, string?> _writableThisSession = new(PathComparer);

    // The UI thread's dispatcher, taken where the gate is made (on the UI thread): requests come on other threads, where reading the
    // static Dispatcher.UIThread while it is unset would make that thread the UI thread.
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    // One question at a time: a script asking for three secrets, or two scripts at once, is asked once per script.
    private readonly SemaphoreSlim _asking = new(1, 1);

    /// <summary>
    /// <see cref="MayAccessAsync"/> on the UI thread, from the thread a script's request arrives on: the question is a dialog, and saving
    /// the settings tells the windows, which update themselves on the thread that owns them.
    /// </summary>
    public Task<bool> MayAccessOnUiThreadAsync(SecretAccess access, CancellationToken cancellationToken)
        => _dispatcher.InvokeAsync(() => MayAccessAsync(access, cancellationToken));

    public async Task<bool> MayAccessAsync(SecretAccess access, CancellationToken cancellationToken)
    {
        var script = access.ScriptPath is { Length: > 0 } path ? Path.GetFullPath(path) : Unsaved;
        var changes = access.IsSave && access.Change != SecretChange.None && confirmChange is not null;
        if (IsAllowed(script) && !changes && (!access.IsSave || CanWrite(script))) return true;
        await _asking.WaitAsync(cancellationToken);
        try
        {
            var name = script == Unsaved ? "an unsaved script" : Path.GetFileName(script);
            // An allowed script replacing or removing a kept secret is asked about that one change. A script asked about just now was told
            // it wants to change the secret, and isn't asked twice.
            if (IsAllowed(script))
            {
                if (!changes)
                {
                    if (access.IsSave) GrantWrite(script);
                    return true;
                }
                if (CanWrite(script)) return true;
                var changeAllowed = await confirmChange!(name, access.Name, access.Change == SecretChange.Remove, cancellationToken)
                    && !cancellationToken.IsCancellationRequested;
                if (changeAllowed) GrantWrite(script);
                return changeAllowed;
            }
            var choice = await ask(name, access.Name, access.IsSave, cancellationToken);
            if (choice == SecretAccessChoice.Deny || cancellationToken.IsCancellationRequested) return false;
            // Always is kept only in the settings, so taking it away there (the Secret Store window) works at once.
            if (choice == SecretAccessChoice.Always && script != Unsaved && _fingerprint(script) is { } contents)
            {
                var settings = preferences.Secrets;
                if (!settings.AllowedScripts.Contains(script, PathComparer)) settings.AllowedScripts.Add(script);
                var key = Stored(settings, script) ?? script;
                settings.AllowedScriptHashes[key] = contents;
                SetWritable(settings, key, access.IsSave);
                preferences.Save();
            }
            else
            {
                lock (_allowedThisSession) _allowedThisSession.Add(script);
                if (access.IsSave) RememberSessionWrite(script);
            }
            return true;
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>
    /// Invoklet saved <paramref name="path"/> with these <paramref name="bytes"/>: an allowed script stays allowed with its new contents.
    /// </summary>
    public void ScriptWritten(string path, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        lock (_writableThisSession)
            if (_writableThisSession.ContainsKey(path)) _writableThisSession[path] = hash;
        if (preferences.Secrets is not { } settings || !settings.AllowedScripts.Contains(path, PathComparer)) return;
        settings.AllowedScriptHashes[Stored(settings, path) ?? path] = hash;
        preferences.Save();
    }

    private bool IsAllowed(string script)
    {
        lock (_allowedThisSession)
            if (_allowedThisSession.Contains(script)) return true;
        return PersistedPermissionKey(script) is not null;
    }

    private bool CanWrite(string script)
    {
        string? allowed;
        bool allowedThisSession;
        lock (_writableThisSession)
        {
            allowedThisSession = _writableThisSession.TryGetValue(script, out allowed);
        }
        // A session grant for older contents of the file does not hide one kept in the settings for the current contents.
        if (allowedThisSession && (script == Unsaved || string.Equals(allowed, _fingerprint(script), StringComparison.OrdinalIgnoreCase)))
            return true;
        return PersistedPermissionKey(script) is { } key
            && preferences.Secrets.WritableScripts.Contains(key, PathComparer);
    }

    private void GrantWrite(string script)
    {
        if (PersistedPermissionKey(script) is { } key)
        {
            var settings = preferences.Secrets;
            if (settings.WritableScripts.Contains(key, PathComparer)) return;
            settings.WritableScripts.Add(key);
            preferences.Save();
            return;
        }
        RememberSessionWrite(script);
    }

    private void RememberSessionWrite(string script)
    {
        var fingerprint = script == Unsaved ? null : _fingerprint(script);
        lock (_writableThisSession) _writableThisSession[script] = fingerprint;
    }

    private string? PersistedPermissionKey(string script)
    {
        if (script == Unsaved || preferences.Secrets is not { } settings || !settings.AllowedScripts.Contains(script, PathComparer)) return null;
        // Allowed as it was: a file changed since by another program, or allowed by a version that kept no contents, is asked about again.
        return Stored(settings, script) is { } key && settings.AllowedScriptHashes.TryGetValue(key, out var allowed)
            && string.Equals(allowed, _fingerprint(script), StringComparison.OrdinalIgnoreCase) ? key : null;
    }

    private static void SetWritable(SecretsSettings settings, string script, bool writable)
    {
        settings.WritableScripts.RemoveAll(path => PathComparer.Equals(path, script));
        if (writable) settings.WritableScripts.Add(script);
    }

    // The key the settings keep the script's contents under, written as it was allowed.
    private static string? Stored(SecretsSettings settings, string path) =>
        settings.AllowedScriptHashes.Keys.FirstOrDefault(k => PathComparer.Equals(k, path)) ?? (settings.AllowedScripts.Contains(path, PathComparer) ? path : null);

    private static string? FileFingerprint(string path)
    {
        try { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
