using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using Invoklet.Contracts;
using Invoklet.Services.Preferences;

namespace Invoklet.Services.Secrets;

/// <summary>
/// Whether a running script may use the Secret Store: the first time a script asks, the user is asked. Always Allow is kept in the
/// settings by the script's full path and the contents of its file, which Invoklet's own saves keep up to date: a file another program
/// changed is asked about again. Read access and read/write access are kept separately; creating a secret or approving one replacement
/// grants write access to that version of the script. Allow Once lasts until Invoklet closes for a saved script, or for the current run
/// for a script without a readable file. A refusal is never remembered.
/// </summary>
/// <param name="confirmChange">Whether an allowed script may replace (or, <c>true</c>, remove) a secret already kept: script name, secret,
/// removes. Without it, an allowed script changes secrets unasked.</param>
/// <param name="fingerprint">The SHA-256 (hex) of a file, or null when it can't be read; by default read from the disk.</param>
/// <param name="dispatcher">The UI thread's dispatcher; by default Avalonia's.</param>
public sealed class SecretAccessGate(IPreferencesService preferences, Func<string, string, bool, CancellationToken, Task<SecretAccessChoice>> ask,
    Func<string, string?>? fingerprint = null, Func<string, string, bool, CancellationToken, Task<bool>>? confirmChange = null,
    IDispatcher? dispatcher = null)
{
    private readonly Func<string, string?> _fingerprint = fingerprint ?? FileFingerprint;

    // Paths are compared as the file system does: letter case aside on Windows and macOS.
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private const string Unsaved = "";

    private sealed record SessionGrant(string? Fingerprint, bool Writable);

    private readonly Dictionary<string, SessionGrant> _sessionGrants = new(PathComparer);
    private readonly ConditionalWeakTable<object, SessionGrant> _runGrants = new();

    // The UI thread's dispatcher, taken where the gate is made (on the UI thread): requests come on other threads, where reading the
    // static Dispatcher.UIThread while it is unset would make that thread the UI thread.
    private readonly IDispatcher _dispatcher = dispatcher ?? Dispatcher.UIThread;

    // One question at a time: a script asking for three secrets, or two scripts at once, is asked once per script.
    private readonly SemaphoreSlim _asking = new(1, 1);

    /// <summary>
    /// <see cref="MayAccessAsync"/> on the UI thread, from the thread a script's request arrives on: the question is a dialog, and saving
    /// the settings tells the windows, which update themselves on the thread that owns them.
    /// </summary>
    public Task<bool> MayAccessOnUiThreadAsync(SecretAccess access, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(async () =>
        {
            try
            {
                answer.TrySetResult(await MayAccessAsync(access, cancellationToken));
            }
            catch (OperationCanceledException ex)
            {
                answer.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                answer.TrySetException(ex);
            }
        });
        return answer.Task;
    }

    public async Task<bool> MayAccessAsync(SecretAccess access, CancellationToken cancellationToken)
    {
        var script = access.ScriptPath is { Length: > 0 } path ? Path.GetFullPath(path) : Unsaved;
        var changes = access.IsSave && access.Change != SecretChange.None && confirmChange is not null;
        var contents = script == Unsaved ? null : _fingerprint(script);
        var session = FindSessionGrant(script, contents, access.RunIdentity);
        var persisted = PersistedPermissionKey(script, contents);
        if (!changes && (session is not null || persisted is not null)
            && (!access.IsSave || CanWrite(session, persisted))) return true;
        await _asking.WaitAsync(cancellationToken);
        try
        {
            contents = script == Unsaved ? null : _fingerprint(script);
            session = FindSessionGrant(script, contents, access.RunIdentity);
            persisted = PersistedPermissionKey(script, contents);
            var name = script == Unsaved ? "an unsaved script" : Path.GetFileName(script);
            // An allowed script replacing or removing a kept secret is asked about that one change. A script asked about just now was told
            // it wants to change the secret, and isn't asked twice.
            if (session is not null || persisted is not null)
            {
                if (!changes)
                {
                    if (access.IsSave) GrantWrite(script, contents, access.RunIdentity, persisted);
                    return true;
                }
                if (CanWrite(session, persisted)) return true;
                var changeAllowed = await confirmChange!(name, access.Name, access.Change == SecretChange.Remove, cancellationToken)
                    && !cancellationToken.IsCancellationRequested;
                if (changeAllowed) GrantWrite(script, contents, access.RunIdentity, persisted);
                return changeAllowed;
            }
            var choice = await ask(name, access.Name, access.IsSave, cancellationToken);
            if (choice == SecretAccessChoice.Deny || cancellationToken.IsCancellationRequested) return false;
            // Always is kept only in the settings, so taking it away there (the Secret Store window) works at once.
            if (choice == SecretAccessChoice.Always && contents is not null)
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
                RememberSessionGrant(script, contents, access.RunIdentity, access.IsSave);
            }
            return true;
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>
    /// Carries a grant forward only when the saved edits started from the script version that was granted access.
    /// </summary>
    public void ScriptWritten(string path, string baselineFingerprint, string savedFingerprint)
    {
        lock (_sessionGrants)
            if (_sessionGrants.TryGetValue(path, out var grant)
                && string.Equals(grant.Fingerprint, baselineFingerprint, StringComparison.OrdinalIgnoreCase))
                _sessionGrants[path] = grant with { Fingerprint = savedFingerprint };
        if (PersistedPermissionKey(path, baselineFingerprint) is not { } key) return;
        preferences.Secrets.AllowedScriptHashes[key] = savedFingerprint;
        preferences.Save();
    }

    private SessionGrant? FindSessionGrant(string script, string? contents, object? run)
    {
        lock (_sessionGrants)
        {
            if (contents is null) return run is not null && _runGrants.TryGetValue(run, out var running) ? running : null;
            return _sessionGrants.TryGetValue(script, out var saved)
                && string.Equals(saved.Fingerprint, contents, StringComparison.OrdinalIgnoreCase) ? saved : null;
        }
    }

    private bool CanWrite(SessionGrant? session, string? persisted) => session?.Writable == true
        || persisted is not null && preferences.Secrets.WritableScripts.Contains(persisted, PathComparer);

    private void GrantWrite(string script, string? contents, object? run, string? persisted)
    {
        if (persisted is { } key)
        {
            var settings = preferences.Secrets;
            if (settings.WritableScripts.Contains(key, PathComparer)) return;
            settings.WritableScripts.Add(key);
            preferences.Save();
            return;
        }
        RememberSessionGrant(script, contents, run, writable: true);
    }

    private void RememberSessionGrant(string script, string? contents, object? run, bool writable)
    {
        lock (_sessionGrants)
        {
            var grant = new SessionGrant(contents, writable);
            if (contents is not null) _sessionGrants[script] = grant;
            else if (run is not null)
            {
                _runGrants.Remove(run);
                _runGrants.Add(run, grant);
            }
        }
    }

    private string? PersistedPermissionKey(string script, string? contents)
    {
        if (contents is null || preferences.Secrets is not { } settings || !settings.AllowedScripts.Contains(script, PathComparer)) return null;
        // Allowed as it was: a file changed since by another program, or allowed by a version that kept no contents, is asked about again.
        return Stored(settings, script) is { } key && settings.AllowedScriptHashes.TryGetValue(key, out var allowed)
            && string.Equals(allowed, contents, StringComparison.OrdinalIgnoreCase) ? key : null;
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
