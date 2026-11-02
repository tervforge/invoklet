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

    public string EnsureOutputDirectory(string filePath, string workDir)
    {
        var key = NormalizeKey(filePath);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var known) && Directory.Exists(known.Directory))
                return known.Directory;

            return Update(entries =>
            {
                // Another instance may have made the folder since this one read the index.
                if (entries.TryGetValue(key, out var entry) && Directory.Exists(entry.Directory))
                    return (entry.Directory, false);

                var dir = Path.Combine(workDir, Path.GetRandomFileName());
                Directory.CreateDirectory(dir);
                entries[key] = new CacheEntry(dir, DateTimeOffset.UtcNow);
                return (dir, true);
            });
        }
    }

    public void Touch(string filePath)
    {
        var key = NormalizeKey(filePath);
        lock (_lock)
        {
            Update(entries =>
            {
                if (!entries.TryGetValue(key, out var entry)) return (0, false);
                entries[key] = entry with { LastUsedUtc = DateTimeOffset.UtcNow };
                return (0, true);
            });
        }
    }

    public void PruneStale(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        lock (_lock)
        {
            Update(entries =>
            {
                var stale = entries.Where(kv => kv.Value.LastUsedUtc < cutoff).ToList();
                foreach (var (key, entry) in stale)
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

                    entries.Remove(key);
                }

                return (0, stale.Count > 0);
            });
        }
    }

    private static string NormalizeKey(string filePath) => Path.GetFullPath(filePath);

    // A change to the index is made on what is in the file now, under a lock on the file that every running Invoklet takes: two instances
    // that read the index once and each wrote their own copy back used to lose each other's entries. Callers hold _lock.
    private T Update<T>(Func<Dictionary<string, CacheEntry>, (T Result, bool Changed)> change)
    {
        using var fileLock = AcquireFileLock();
        Load();
        var (result, changed) = change(_entries);
        if (changed) Save();
        return result;
    }

    // The lock file is kept (never deleted), so that every writer locks the same file. If it cannot be had the change is made without it:
    // opening a script must not fail for the sake of a cache.
    private FileStream? AcquireFileLock()
    {
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var path = _cachePath + ".lock";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                return new FileStream(path, options);
            }
            catch (IOException) when (attempt < 200)
            {
                Thread.Sleep(25); // another instance is writing; it takes a moment
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not lock the build cache; changing it without the lock. Path: {Path}", path);
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
