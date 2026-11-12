using System.Security.Cryptography;

namespace Invoklet.Secrets;

/// <summary>
/// A store kept as one file that several Invoklet windows and the command line write: replaced whole under a lock, only while it is
/// still the version its writer loaded, and readable by its owner only. The Secret Store and the Path Store both keep their files so.
/// </summary>
internal static class AtomicStoreFile
{
    /// <summary>The file's bytes; null when there is none.</summary>
    public static byte[]? Read(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>The version of a file's contents, as a writer compares it.</summary>
    public static string VersionOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Replaces the file at <paramref name="filePath"/> with what <paramref name="build"/> makes of its current bytes (null: no file),
    /// only if it still is <paramref name="expectedVersion"/> (null: no file); returns the new version.
    /// </summary>
    /// <param name="conflict">Thrown when another writer changed the file; nothing is written.</param>
    /// <param name="busy">Thrown, around the last failure, when the file stayed in use; nothing is written.</param>
    public static string Replace(string filePath, string? expectedVersion, Func<string, byte[]?, byte[]> build,
        Func<Exception> conflict, Func<IOException, Exception> busy)
    {
        // A store that is a symbolic link (into a synced or shared folder, say) stays one: the file it
        // points to is replaced, not the link.
        var fullPath = StoreTarget(Path.GetFullPath(filePath));
        var folder = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(folder);
        var windows = OperatingSystem.IsWindows();
        // Keep this lock file: deleting it would let concurrent writers lock different inodes.
        var lockOptions = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!windows) lockOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var writer = WhileInUse(() => new FileStream(fullPath + ".lock", lockOptions), busy);
        var current = Read(fullPath);
        var currentVersion = current is null ? null : VersionOf(current);
        if (!string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            throw conflict();
        DeleteLeftoverTemps(folder, Path.GetFileName(fullPath));
        // Under the lock, so what the store adds to its contents (a new key, say) is made by one writer only.
        var bytes = build(fullPath, current);

        // Written next to the store and moved over it, so a failed write never leaves half a store;
        // created readable by its owner only, before anything is in it. Named after the store, so
        // one left by a process killed before the move is recognised and removed by the next save.
        var temp = Path.Combine(folder, TempPrefix(Path.GetFileName(fullPath)) + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!windows) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            ReplaceStore(temp, fullPath, busy);
            return VersionOf(bytes);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // The file a store path finally points to, following symbolic links; the path itself otherwise.
    private static string StoreTarget(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path; // a link cycle: writing over the link reports the problem itself
        }
    }

    private static string TempPrefix(string storeName) => storeName + ".";

    // Called under the lock, so no other save is between writing and moving its own temp file.
    private static void DeleteLeftoverTemps(string folder, string storeName)
    {
        var prefix = TempPrefix(storeName);
        const string suffix = ".tmp";
        foreach (var path in Directory.EnumerateFiles(folder, "*.tmp"))
        {
            var name = Path.GetFileName(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (name.Length != prefix.Length + 32 + suffix.Length || !name.StartsWith(prefix, comparison)
                || !name.EndsWith(suffix, comparison) || !Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out _)) continue;
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // On Windows a script reading the store (Util.GetSecret) keeps it open for a moment, and replacing
    // an open file fails; it is retried like the lock. Access denied persisting with the file free is a
    // real permission problem and is reported as such.
    private static void ReplaceStore(string temp, string target, Func<IOException, Exception> busy) => WhileInUse(() =>
    {
        try
        {
            File.Move(temp, target, overwrite: true);
        }
        catch (UnauthorizedAccessException ex) when (OperatingSystem.IsWindows() && IsOpenElsewhere(target))
        {
            throw new IOException(ex.Message, unchecked((int)0x80070020)); // ERROR_SHARING_VIOLATION
        }
        return true;
    }, busy);

    private static bool IsOpenElsewhere(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException ex) when (IsInUse(ex)) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // Retries for under a second while the file is held by another writer or reader, then gives up
    // with busy's exception; any other failure is thrown at once.
    private static T WhileInUse<T>(Func<T> action, Func<IOException, Exception> busy)
    {
        const int attempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException ex) when (IsInUse(ex))
            {
                if (attempt == attempts) throw busy(ex);
                Thread.Sleep(50 * attempt);
            }
        }
    }

    // A sharing or lock violation on Windows; on macOS and Linux, .NET's FileShare.None lock failing
    // with EWOULDBLOCK (35 on macOS and the BSDs, 11 on Linux).
    private static bool IsInUse(IOException ex) => OperatingSystem.IsWindows()
        ? (ex.HResult & 0xFFFF) is 32 or 33
        : ex.HResult is 11 or 35;
}
