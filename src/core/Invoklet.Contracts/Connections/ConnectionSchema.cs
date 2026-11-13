using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Invoklet.Format.MdRun;

namespace Invoklet.Contracts.Connections;

/// <summary>A C# file a connection adds to the script's build and editor, beside the Connections class.</summary>
public sealed record GeneratedSourceFile(string Name, string Text);

/// <summary>
/// What a driver learned from the endpoint itself, such as a gRPC server's services: the C# that
/// describes them (message and client classes) and the driver's own notes for writing the client.
/// </summary>
/// <param name="Summary">A line for the user, such as "2 services: inventory.v1.Stock, …".</param>
/// <param name="Files">C# files compiled with the script.</param>
/// <param name="Data">The driver's own data for <see cref="ICSharpClientSource.CreateClient"/>.</param>
public sealed record ConnectionSchema(string Summary, IReadOnlyList<GeneratedSourceFile> Files, string Data);

/// <summary>What a driver may use while reading a schema: NuGet packages with build tools, for instance.</summary>
public sealed record ConnectionTools
{
    /// <summary>The folder of a NuGet package (id, version), downloaded first when it isn't there yet.</summary>
    public required Func<string, string, CancellationToken, Task<string>> GetPackageFolderAsync { get; init; }

    /// <summary>The folder of the script the schema is read for, which relative file paths in the
    /// parameters are read from; null for a script never saved, or outside a script.</summary>
    public string? ScriptDirectory { get; init; }
}

/// <summary>
/// A driver whose client depends on the endpoint's own description, read by Invoklet (not by the
/// script) before the script is built: a gRPC server's services through server reflection.
/// </summary>
public interface IConnectionSchemaDriver : IConnectionDriver
{
    /// <summary>Reads the endpoint's description with placeholders already replaced. Throws with a
    /// message for the user when the endpoint can't be reached or described.</summary>
    Task<ConnectionSchema> FetchSchemaAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the schema depends on the script's folder, as a database file given by a relative
    /// path does: such a connection's schema is kept for each folder it is used from.
    /// </summary>
    bool UsesScriptDirectory(MdRunConnection connection) => false;

    /// <summary>Change when cached descriptions need to be read again after a driver update.</summary>
    string SchemaVersion => "";
}

/// <summary>Which schemas <see cref="ConnectionSchemaCache.EnsureAsync"/> reads.</summary>
public enum ConnectionSchemaRefresh
{
    /// <summary>Those never read and never failed: what the editor does in the background, so an
    /// unreachable server isn't asked again on every edit. A connection with a <c>${secret:…}</c> or <c>${env:…}</c>
    /// is left to a run or a Refresh, so opening a script never sends one to the server it names.</summary>
    Missing,

    /// <summary>Those not cached, failed ones included: before a run.</summary>
    Failed,

    /// <summary>All of them again: the Refresh and Test buttons.</summary>
    All,
}

/// <summary>
/// The schemas Invoklet has read, kept on disk by the connection's driver and parameters (as written,
/// placeholders included, so no secret value is part of the key) and, for a connection whose driver
/// says it depends on it, the script's folder; and the last reason one couldn't be read. Shared by
/// the window, the editor and the command-line runner.
/// </summary>
/// <param name="drivers">Tells which connections depend on the script's folder; none do without it.</param>
/// <summary>The schemas the generated code of the connections is written from (<see cref="GeneratedConnections.For"/>).</summary>
public interface IConnectionSchemaSource
{
    /// <summary>The schema, read from wherever it is kept; null when there is none.</summary>
    ConnectionSchema? Get(MdRunConnection connection, string? scriptDirectory = null);

    /// <summary>The schema already at hand, without reading anything; null when there is none.</summary>
    ConnectionSchema? TryGetLoaded(MdRunConnection connection, string? scriptDirectory = null);

    /// <summary>Why the schema couldn't be read the last time, or null.</summary>
    string? ErrorFor(MdRunConnection connection, string? scriptDirectory = null);
}

public sealed class ConnectionSchemaCache(string folder, ConnectionDriverRegistry? drivers = null) : IConnectionSchemaSource
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    // The reads under way, by key: the editor, a run and the Connections window often ask for the
    // same schema at once, and each read may be a server round trip plus a code generator.
    private readonly Dictionary<string, ActiveFetch> _fetches = new(StringComparer.Ordinal);
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);
    private sealed record ActiveFetch(string Fingerprint, object Generation, Task<bool> Task);
    // Generated contracts can be large. Keep a bounded hot set, invalidated by file metadata,
    // instead of reparsing the same JSON during every editor/context refresh.
    private readonly Dictionary<string, CachedDescription> _descriptions = new(StringComparer.Ordinal);
    private sealed record CachedDescription(long Written, long Created, long Length, ConnectionSchema Schema);
    private readonly object _lock = new();

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet", "connection-schemas");

    public string Folder { get; } = folder;

    /// <summary>
    /// A schema was written, or a read of one failed, with the connection's <see cref="KeyOf"/>.
    /// Raised on the thread that did it, often a worker thread, so an editor showing that connection's
    /// classes can catch up with a read it didn't start (another window's Refresh, for instance).
    /// </summary>
    public event Action<string>? Changed;

    /// <summary>A read of a schema started or ended (<see cref="IsFetching"/>), with its key; raised on any thread.</summary>
    public event Action<string>? FetchingChanged;

    public string KeyOf(MdRunConnection connection, string? scriptDirectory = null)
    {
        var folderPart = drivers?.Find(connection.Driver) is IConnectionSchemaDriver driver && driver.UsesScriptDirectory(connection)
            ? "\n" + (scriptDirectory is null ? "" : Path.GetFullPath(scriptDirectory))
            : "";
        var versionPart = drivers?.Find(connection.Driver) is IConnectionSchemaDriver { SchemaVersion.Length: > 0 } versioned
            ? "\nversion:" + versioned.SchemaVersion : "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            connection.Driver.ToLowerInvariant() + "\n" + JsonSerializer.Serialize(connection.Params) + folderPart + versionPart))).ToLowerInvariant()[..32];
    }

    /// <summary>The cached schema, or null when there is none (or it can't be read).</summary>
    public ConnectionSchema? Get(MdRunConnection connection, string? scriptDirectory = null)
    {
        var key = KeyOf(connection, scriptDirectory);
        var path = Path.Combine(Folder, key + ".json");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                lock (_lock) _descriptions.Remove(key);
                return null;
            }
            var written = info.LastWriteTimeUtc.Ticks;
            var created = info.CreationTimeUtc.Ticks;
            var length = info.Length;
            lock (_lock)
                if (_descriptions.TryGetValue(key, out var cached)
                    && cached.Written == written && cached.Created == created && cached.Length == length)
                    return cached.Schema;

            // File access and JSON parsing happen outside the cache lock. The editor can keep using
            // an already loaded schema while another caller refreshes a cold or changed entry.
            var schema = JsonSerializer.Deserialize<ConnectionSchema>(File.ReadAllText(path));
            if (schema is null)
            {
                lock (_lock) _descriptions.Remove(key);
                return null;
            }
            lock (_lock)
            {
                if (_descriptions.TryGetValue(key, out var cached)
                    && cached.Written == written && cached.Created == created && cached.Length == length)
                    return cached.Schema;
                // A refresh may have replaced this file while it was being read.
                info.Refresh();
                if (info.Exists && info.LastWriteTimeUtc.Ticks == written && info.CreationTimeUtc.Ticks == created && info.Length == length)
                    Remember(key, new CachedDescription(written, created, length, schema));
            }
            return schema;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            lock (_lock) _descriptions.Remove(key);
            return null;
        }
    }

    /// <summary>Returns a schema already loaded in this process, without checking or reading disk.</summary>
    public ConnectionSchema? TryGetLoaded(MdRunConnection connection, string? scriptDirectory = null)
    {
        var key = KeyOf(connection, scriptDirectory);
        lock (_lock) return _descriptions.TryGetValue(key, out var cached) ? cached.Schema : null;
    }

    /// <summary>Whether this Invoklet is reading the connection's schema right now.</summary>
    public bool IsFetching(MdRunConnection connection, string? scriptDirectory = null)
    {
        var key = KeyOf(connection, scriptDirectory);
        lock (_lock) return _fetches.ContainsKey(key);
    }

    /// <summary>Why the schema couldn't be read the last time this Invoklet tried, or null.</summary>
    public string? ErrorFor(MdRunConnection connection, string? scriptDirectory = null)
    {
        lock (_lock) return _errors.GetValueOrDefault(KeyOf(connection, scriptDirectory));
    }

    /// <summary>
    /// Deletes schema files not written in <paramref name="maxAge"/>: a connection removed, renamed
    /// or changed since leaves its old key's file behind forever, as nothing else in this cache ever
    /// revisits a key once its connection stops asking for it. A key still in use keeps being
    /// written (or at least re-read successfully) well inside that window, so this never drops a
    /// live schema for long: the worst case is one extra fetch the next time it's needed. A leftover
    /// `.tmp` file (the process killed between write and rename in <see cref="Put"/>) is removed
    /// regardless of age, since a successful `Put` always cleans up its own.
    /// </summary>
    public void PruneStale(TimeSpan maxAge)
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var path in Directory.EnumerateFiles(Folder, "*.tmp"))
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;
                File.Delete(path);
                var key = Path.GetFileNameWithoutExtension(path);
                lock (_lock) { _descriptions.Remove(key); _errors.Remove(key); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Put(MdRunConnection connection, ConnectionSchema schema, string? scriptDirectory = null)
    {
        var key = KeyOf(connection, scriptDirectory);
        if (TryStore(key, schema, generation: null))
            Changed?.Invoke(key);
    }

    private bool TryStore(string key, ConnectionSchema schema, object? generation)
    {
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, key + ".json");
        // Named after the key, so one left over (the process killed mid-write) tells whose it was.
        var temp = Path.Combine(Folder, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(schema));
            try { return Commit(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // On Windows another Invoklet (the window and the CLI share the folder) may be reading
                // the file for a moment.
                Thread.Sleep(100);
                return Commit();
            }
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        bool Commit()
        {
            lock (_lock)
            {
                if (generation is not null && !IsCurrentFetch(key, generation)) return false;
                File.Move(temp, target, overwrite: true);
                _errors.Remove(key);
                var info = new FileInfo(target);
                if (info.Exists)
                    Remember(key, new CachedDescription(info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, info.Length, schema));
                else
                    _descriptions.Remove(key);
                return true;
            }
        }
    }

    // Called with the cache lock held.
    private bool IsCurrentFetch(string key, object generation) =>
        _fetches.TryGetValue(key, out var fetch) && ReferenceEquals(fetch.Generation, generation);

    private void Remember(string key, CachedDescription description)
    {
        if (!_descriptions.ContainsKey(key) && _descriptions.Count >= 16)
            _descriptions.Remove(_descriptions.Keys.First());
        _descriptions[key] = description;
    }

    /// <summary>
    /// Reads and caches the schema of the connections that need one, as <paramref name="refresh"/>
    /// says which. Returns true when any schema or error changed. Failures are kept for
    /// <see cref="ErrorFor"/>; a missing secret or variable is such a failure too. A schema already
    /// being read for another caller isn't read a second time: this call waits for that read.
    /// Cancelling stops the wait, not the read, which still records its schema or error.
    /// </summary>
    public async Task<bool> EnsureAsync(IEnumerable<MdRunConnection> connections, string? scriptDirectory,
        ConnectionDriverRegistry drivers, Func<string, string?> secret, ConnectionTools tools, ConnectionSchemaRefresh refresh,
        CancellationToken cancellationToken)
    {
        var changed = false;
        tools = tools with { ScriptDirectory = scriptDirectory };
        foreach (var connection in connections)
        {
            if (drivers.Find(connection.Driver) is not IConnectionSchemaDriver driver || driver.Validate(connection).Count > 0) continue;
            if (refresh != ConnectionSchemaRefresh.All && Get(connection, scriptDirectory) is not null) continue;
            if (refresh == ConnectionSchemaRefresh.Missing && ErrorFor(connection, scriptDirectory) is not null) continue;
            // What the editor reads on its own, as a script is opened, must not send a secret or a variable to the server the file names: that
            // waits for a run or a Refresh, which the user asked for.
            if (refresh == ConnectionSchemaRefresh.Missing && UsesPlaceholders(connection)) continue;
            var key = KeyOf(connection, scriptDirectory);
            var resolved = ConnectionPlaceholderResolver.Resolve(connection.Params, secret, Environment.GetEnvironmentVariable);
            if (resolved.Errors.Count > 0)
            {
                bool superseded;
                lock (_lock)
                {
                    superseded = _fetches.Remove(key);
                    _errors[key] = string.Join(" ", resolved.Errors);
                }
                if (superseded) FetchingChanged?.Invoke(key);
                Changed?.Invoke(key);
                changed = true;
                continue;
            }
            if (await FetchOnceAsync(key, connection, driver, resolved.Params, tools, scriptDirectory, refresh)
                .WaitAsync(cancellationToken).ConfigureAwait(false))
                changed = true;
        }
        return changed;
    }

    private static bool UsesPlaceholders(MdRunConnection connection)
    {
        var used = false;
        ConnectionPlaceholderResolver.Resolve(connection.Params, _ => { used = true; return ""; }, _ => { used = true; return ""; });
        return used;
    }

    // The read under way for this key, or a new one. It runs on the thread pool, as some drivers read
    // synchronously, and without any caller's token: it is shared, so one caller giving up must not
    // cancel it for the others, nor record the cancellation as the schema's error. False when there
    // was nothing to read after all: a read that finished just before this call came for it.
    private Task<bool> FetchOnceAsync(string key, MdRunConnection connection, IConnectionSchemaDriver driver,
        IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools, string? scriptDirectory,
        ConnectionSchemaRefresh refresh)
    {
        var cached = refresh != ConnectionSchemaRefresh.All ? Get(connection, scriptDirectory) : null;
        // A changed secret or variable needs a fresh read. Keep only a process-keyed fingerprint
        // of the resolved parameters in the request registry.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(resolvedParams);
        string fingerprint;
        try { fingerprint = Convert.ToHexString(HMACSHA256.HashData(FingerprintKey, bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var generation = new object();
        Task<bool> fetch;
        lock (_lock)
        {
            if (_fetches.TryGetValue(key, out var running) && running.Fingerprint == fingerprint)
                return running.Task;
            if (refresh != ConnectionSchemaRefresh.All && (cached is not null || _descriptions.ContainsKey(key)))
                return Task.FromResult(false);
            if (refresh == ConnectionSchemaRefresh.Missing && _errors.ContainsKey(key)) return Task.FromResult(false);
            fetch = Task.Run(() => ReadAndPublishAsync(key, generation, driver, resolvedParams, tools));
            _fetches[key] = new ActiveFetch(fingerprint, generation, fetch);
        }
        FetchingChanged?.Invoke(key);
        return fetch;
    }

    private async Task<bool> ReadAndPublishAsync(string key, object generation, IConnectionSchemaDriver driver,
        IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools)
    {
        try
        {
            try
            {
                var schema = await driver.FetchSchemaAsync(resolvedParams, tools, CancellationToken.None).ConfigureAwait(false);
                if (!TryStore(key, schema, generation)) return false;
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    if (!IsCurrentFetch(key, generation)) return false;
                    _errors[key] = ex.Message;
                }
            }
            Changed?.Invoke(key);
            return true;
        }
        finally
        {
            bool ended;
            lock (_lock)
            {
                ended = IsCurrentFetch(key, generation) && _fetches.Remove(key);
            }
            if (ended) FetchingChanged?.Invoke(key);
        }
    }
}

/// <summary>
/// Everything Invoklet needs to read connection schemas: the drivers, the cache, the tools and where
/// secrets come from (the Secret Store in the app, the same file through the runtime in the CLI).
/// </summary>
public sealed class ConnectionSchemas(ConnectionDriverRegistry drivers, string cacheFolder, ConnectionTools tools,
    Func<string, string?> secret)
{
    public ConnectionDriverRegistry Drivers { get; } = drivers;

    public ConnectionSchemaCache Cache { get; } = new(cacheFolder, drivers);

    /// <param name="scriptDirectory">The script's folder, for relative file paths; null outside a saved script.</param>
    public Task<bool> EnsureAsync(IEnumerable<MdRunConnection> connections, string? scriptDirectory, ConnectionSchemaRefresh refresh,
        CancellationToken cancellationToken) =>
        Cache.EnsureAsync(connections, scriptDirectory, Drivers, secret, tools, refresh, cancellationToken);
}
