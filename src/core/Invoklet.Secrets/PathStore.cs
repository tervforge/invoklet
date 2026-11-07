using System.Text.Json;

namespace Invoklet.Secrets;

/// <summary>A named path: a folder or file on this machine a script reads with <c>Util.GetPath</c>, always a full path.</summary>
public sealed record NamedPath(string Name, string Path);

/// <summary>Named paths and the exact file generation they were read from; null means no file existed.</summary>
public sealed record PathStoreSnapshot(IReadOnlyList<NamedPath> Paths, string? Version);

public sealed class PathStoreConflictException() : IOException("The path store changed after it was opened.");

/// <summary>Another Invoklet window is saving the store; trying again shortly usually succeeds. Nothing was written.</summary>
public sealed class PathStoreBusyException(Exception inner) : IOException("The path store is in use.", inner);

/// <summary>
/// Invoklet's Path Store: where things are on this machine for this user (a workspace, a tool, a data folder), kept outside every
/// script, so a script names a path instead of containing one. Not a secret store: nothing in it is encrypted.
/// </summary>
public interface IPathStore
{
    /// <summary>The store's file.</summary>
    string FilePath { get; }

    /// <summary>Every path, in the order they were saved; empty when there is no store yet.</summary>
    /// <exception cref="InvalidDataException">The file is damaged or from a newer Invoklet. It is left as it is.</exception>
    PathStoreSnapshot LoadSnapshot();

    /// <summary>Replaces the store only if it still matches the loaded version; returns the new version.</summary>
    /// <exception cref="ArgumentException">A name or path isn't valid (<see cref="PathStoreRules"/>); nothing was written.</exception>
    /// <exception cref="PathStoreConflictException">Another writer changed the file; nothing was written.</exception>
    /// <exception cref="PathStoreBusyException">The file stayed in use; nothing was written.</exception>
    string Save(IReadOnlyList<NamedPath> paths, string? expectedVersion);
}

/// <summary>What a name and a path must be, the same for the window and a script.</summary>
public static class PathStoreRules
{
    private const int MaxNameLength = 100;

    /// <summary>The name trimmed; letters, digits, <c>_</c>, <c>-</c> and <c>.</c>, compared letter case aside.</summary>
    /// <exception cref="ArgumentException">The name is empty, too long or has another character.</exception>
    public static string Name(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) throw new ArgumentException("A path needs a name.");
        if (trimmed.Length > MaxNameLength) throw new ArgumentException($"A path name has at most {MaxNameLength} characters.");
        if (!trimmed.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
            throw new ArgumentException($"The path name '{trimmed}' may have only letters, digits, '_', '-' and '.'.");
        return trimmed;
    }

    /// <summary>
    /// The full path <paramref name="value"/> stands for, <c>~</c> being the user's home folder. Kept as written otherwise: links are not
    /// followed, and the path need not exist.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, has a character no path can, or is relative.</exception>
    public static string Resolve(string? value)
    {
        var path = value?.Trim() ?? "";
        if (path.Length == 0) throw new ArgumentException("A path can't be empty.");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException($"'{path}' isn't a valid path.");
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || OperatingSystem.IsWindows() && path.StartsWith(@"~\", StringComparison.Ordinal))
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..]);
        else if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"'{path}' is relative: give a full path, or one starting with ~ for your home folder.");
        var full = Path.GetFullPath(path);
        return Path.GetPathRoot(full) == full ? full : Path.TrimEndingDirectorySeparator(full);
    }
}

public static class PathStoreExtensions
{
    /// <summary>The path saved under a name, letter case aside; null when the store has none.</summary>
    /// <exception cref="ArgumentException">The name isn't valid.</exception>
    /// <exception cref="InvalidDataException">The store can't be read.</exception>
    public static string? Read(this IPathStore store, string name)
    {
        var valid = PathStoreRules.Name(name);
        return store.LoadSnapshot().Paths.FirstOrDefault(p => string.Equals(p.Name, valid, StringComparison.OrdinalIgnoreCase))?.Path;
    }

    /// <summary>
    /// Saves a path under a name, or replaces the one of that name in any case where it stands; a null value removes it. The value is
    /// resolved first (<see cref="PathStoreRules.Resolve"/>); returns what was saved. Saved again on the newer store when another
    /// window saved first.
    /// </summary>
    /// <exception cref="ArgumentException">The name or the path isn't valid.</exception>
    public static string? Put(this IPathStore store, string name, string? value)
    {
        var valid = PathStoreRules.Name(name);
        var path = value is null ? null : PathStoreRules.Resolve(value);
        for (var attempt = 1; ; attempt++)
        {
            var snapshot = store.LoadSnapshot();
            var paths = snapshot.Paths.ToList();
            var index = paths.FindIndex(p => string.Equals(p.Name, valid, StringComparison.OrdinalIgnoreCase));
            if (path is null)
            {
                if (index < 0) return null;
                paths.RemoveAt(index);
            }
            else if (index >= 0) paths[index] = paths[index] with { Path = path };
            else paths.Add(new NamedPath(valid, path));
            try
            {
                store.Save(paths, snapshot.Version);
                return path;
            }
            catch (PathStoreConflictException) when (attempt < 3) { }
        }
    }
}

/// <summary>
/// The store as one JSON file, by default <c>paths.json</c> in Invoklet's data folder beside the Secret Store (or <see cref="FileEnvVar"/>),
/// readable by its owner only. A script never reads it: it asks Invoklet for one path by name.
/// </summary>
public sealed class FilePathStore(string? filePath = null) : IPathStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string FilePath { get; } = filePath ?? DefaultPath();

    /// <summary>Points Invoklet and the command line at another file, as <c>INVOKLET_SECRETS_FILE</c> does for the Secret Store.</summary>
    public const string FileEnvVar = "INVOKLET_PATHS_FILE";

    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable(FileEnvVar) is { Length: > 0 } path
            ? path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet", "paths.json");

    public PathStoreSnapshot LoadSnapshot()
    {
        var bytes = AtomicStoreFile.Read(FilePath);
        if (bytes is null) return new PathStoreSnapshot([], null);
        StoreFile file;
        try
        {
            file = JsonSerializer.Deserialize<StoreFile>(bytes) ?? throw new InvalidDataException("The path store is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The path store file is damaged.", ex);
        }
        if (file.Version > FormatVersion)
            throw new InvalidDataException($"The path store was written by a newer Invoklet (format {file.Version}).");
        if (file.Version != FormatVersion)
            throw new InvalidDataException("The path store file is damaged.");
        var paths = new List<NamedPath>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in file.Paths ?? [])
        {
            if (path is null || !Path.IsPathFullyQualified(path) || !IsValidName(name))
                throw new InvalidDataException("The path store file is damaged.");
            // Util.GetPath finds a path by name letter case aside: names differing only in case would make the path it reads
            // depend on the file's order.
            if (!names.Add(name))
                throw new InvalidDataException($"The path store file is damaged: two paths are named '{name}' (letter case aside).");
            paths.Add(new NamedPath(name, path));
        }
        return new PathStoreSnapshot(paths, AtomicStoreFile.VersionOf(bytes));
    }

    public string Save(IReadOnlyList<NamedPath> paths, string? expectedVersion)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (PathStoreRules.Name(path.Name) != path.Name) throw new ArgumentException($"The path name '{path.Name}' has spaces around it.", nameof(paths));
            if (!Path.IsPathFullyQualified(path.Path)) throw new ArgumentException($"The path '{path.Path}' of '{path.Name}' isn't a full path.", nameof(paths));
            if (!names.Add(path.Name)) throw new ArgumentException($"Two paths are named '{path.Name}' (letter case aside).", nameof(paths));
        }
        var file = new StoreFile { Version = FormatVersion, Paths = paths.ToDictionary(p => p.Name, string? (p) => p.Path) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, WriteOptions);
        return AtomicStoreFile.Replace(FilePath, expectedVersion, (_, _) => bytes,
            () => new PathStoreConflictException(), ex => new PathStoreBusyException(ex));
    }

    private static bool IsValidName(string name)
    {
        try { return PathStoreRules.Name(name) == name; }
        catch (ArgumentException) { return false; }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; }
        public Dictionary<string, string?>? Paths { get; set; }
    }
}
