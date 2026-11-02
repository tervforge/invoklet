using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Invoklet.Services.BuildCache;

public sealed class BuildCacheService : IBuildCacheService
{
    private static readonly string DefaultCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Invoklet", "build-cache.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    // Guards _entries and every read/write of the backing file below — EnsureOutputDirectory/Touch
    // run on the UI thread while a tab opens/saves, and App.axaml.cs's startup PruneStale runs
    // concurrently on a background Task.Run; without this, concurrent mutation of the plain
    // Dictionary is undefined behavior. Another running Invoklet shares the file: see Update.
    private readonly Lock _lock = new();
    private readonly ILogger<BuildCacheService> _logger;
    private readonly string _cachePath;
    private Dictionary<string, CacheEntry> _entries = new(FilePaths.Comparer);

    public BuildCacheService(ILogger<BuildCacheService> logger) : this(logger, DefaultCachePath) { }

    // Internal ctor with an injectable cache-file path so tests can point this at an isolated temp
    // file instead of the real %APPDATA%/Invoklet directory.
    internal BuildCacheService(ILogger<BuildCacheService> logger, string cachePath)
    {
        _logger = logger;
        _cachePath = cachePath;
        Load();
    }

    // How long a tab waits for another running Invoklet that is changing the index; it is a moment, not something to hold the window for.
    private static readonly TimeSpan UiLockWait = TimeSpan.FromMilliseconds(200);

    // The startup clean-up runs off the UI thread and can wait for an instance that is busy.
    internal TimeSpan BackgroundLockWait { get; init; } = TimeSpan.FromSeconds(30);

    // Folders made while the index could not be locked: the script has its folder, and the index gets its entry with the next change
    // that gets the lock. The shared file is never changed without the lock.
    private readonly Dictionary<string, CacheEntry> _pending = new(FilePaths.Comparer);

    public string EnsureOutputDirectory(string filePath, string workDir)
    {
        var key = NormalizeKey(filePath);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var known) && Directory.Exists(known.Directory))
                return known.Directory;

            if (TryUpdate(entries =>
                {
                    // Another instance may have made the folder since this one read the index.
                    if (entries.TryGetValue(key, out var entry) && Directory.Exists(entry.Directory))
                        return (entry.Directory, false);

                    var dir = NewDirectory(workDir);
                    entries[key] = new CacheEntry(dir, DateTimeOffset.UtcNow);
                    return (dir, true);
                }, UiLockWait, out var directory))
                return directory!;

            var local = new CacheEntry(NewDirectory(workDir), DateTimeOffset.UtcNow);
            _entries[key] = local;
            _pending[key] = local;
            return local.Directory;
        }
    }

    public void Touch(string filePath)
    {
        var key = NormalizeKey(filePath);
        lock (_lock)
        {
            // When the index is locked by another instance the time of use is not updated: it is only for the clean-up of old folders.
            TryUpdate(entries =>
            {
                if (!entries.TryGetValue(key, out var entry)) return (0, false);
                entries[key] = entry with { LastUsedUtc = DateTimeOffset.UtcNow };
                return (0, true);
            }, UiLockWait, out _);
        }
    }

    public void PruneStale(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        List<CacheEntry> removed;
        // The index is changed under the lock of its file, briefly; the folders are deleted after it is let go, and without the lock of
        // this instance, so that a tab opening meanwhile does not wait for a clean-up that is deleting folders.
        using (var fileLock = AcquireFileLock(BackgroundLockWait))
        {
            if (fileLock is null) return;
            lock (_lock)
            {
                Load();
                var merged = MergePending(_entries);
                var stale = _entries.Where(kv => kv.Value.LastUsedUtc < cutoff).ToList();
                foreach (var (key, _) in stale) _entries.Remove(key);
                if (stale.Count > 0 || merged) Save();
                removed = [.. stale.Select(kv => kv.Value)];
            }
        }

        foreach (var entry in removed)
        {
            try
            {
                if (Directory.Exists(entry.Directory))
                    Directory.Delete(entry.Directory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete stale build cache directory {Directory}", entry.Directory);
            }
        }
    }

    private static string NewDirectory(string workDir)
    {
        var dir = Path.Combine(workDir, Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string NormalizeKey(string filePath) => Path.GetFullPath(filePath);

    // A change to the index is made on what is in the file now, under a lock on the file that every running Invoklet takes: two instances
    // that read the index once and each wrote their own copy back used to lose each other's entries. False when the lock was not had
    // within <paramref name="wait"/>: nothing is changed then. Callers hold _lock.
    private bool TryUpdate<T>(Func<Dictionary<string, CacheEntry>, (T Result, bool Changed)> change, TimeSpan wait, out T? result)
    {
        using var fileLock = AcquireFileLock(wait);
        if (fileLock is null)
        {
            result = default;
            return false;
        }

        Load();
        var merged = MergePending(_entries);
        var (value, changed) = change(_entries);
        if (changed || merged) Save();
        result = value;
        return true;
    }

    // The folders made while the lock could not be had are entered now, unless another instance has entered a folder for the script since.
    private bool MergePending(Dictionary<string, CacheEntry> entries)
    {
        var changed = false;
        foreach (var (key, entry) in _pending)
        {
            if (entries.TryGetValue(key, out var existing) && Directory.Exists(existing.Directory)) continue;
            entries[key] = entry;
            changed = true;
        }
        _pending.Clear();
        return changed;
    }

    // The lock file is kept (never deleted), so that every writer locks the same file. Null: the lock was not had in time, or cannot be had.
    private FileStream? AcquireFileLock(TimeSpan wait)
    {
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var path = _cachePath + ".lock";
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                return new FileStream(path, options);
            }
            catch (IOException) when (waited.Elapsed < wait)
            {
                Thread.Sleep(20); // another instance is writing; it takes a moment
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "The build cache is locked or cannot be locked; it is not changed. Path: {Path}", path);
                return null;
            }
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        AtomicFile.WriteAllBytes(_cachePath, JsonSerializer.SerializeToUtf8Bytes(_entries, SerializerOptions));
    }

    private void Load()
    {
        if (!File.Exists(_cachePath)) return;
        try
        {
            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
            if (loaded is not null)
                _entries = new Dictionary<string, CacheEntry>(loaded, FilePaths.Comparer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Build cache file is corrupt; starting empty. Path: {Path}", _cachePath);
        }
    }

    private sealed record CacheEntry(string Directory, DateTimeOffset LastUsedUtc);
}
