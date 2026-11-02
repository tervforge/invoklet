namespace Invoklet.Services;

/// <summary>
/// Writes a file so that a write that fails leaves the file as it was: the bytes go to a temporary file in the same folder, which then
/// replaces the target. The target keeps its permission bits (macOS and Linux) and, when it is a symbolic link, stays one: the file it
/// points to is replaced. The temporary file is readable by its owner only from the moment it exists, so that the content of a private file
/// is never readable by others while it is being written. A failure (disk full, a lost network share, no permission) throws IOException
/// or UnauthorizedAccessException.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] bytes) => WriteAllBytes(path, bytes, whileTempIsOpen: null);

    // whileTempIsOpen is told the temporary file when it exists and nothing has been written to it: for a test that looks at it then.
    internal static void WriteAllBytes(string path, byte[] bytes, Action<string>? whileTempIsOpen)
    {
        var target = Resolve(path);
        RefuseReadOnly(target);
        var temp = TempFor(target);
        try
        {
            using (var stream = new FileStream(temp, Options(useAsync: false)))
            {
                whileTempIsOpen?.Invoke(temp);
                stream.Write(bytes);
                // Without it a crash just after the replacement can leave a file of zero bytes on some file systems.
                stream.Flush(flushToDisk: true);
            }
            Replace(temp, target);
        }
        finally
        {
            Discard(temp);
        }
    }

    public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        => WriteAllBytesAsync(path, bytes, whileTempIsOpen: null, cancellationToken);

    internal static async Task WriteAllBytesAsync(string path, byte[] bytes, Action<string>? whileTempIsOpen, CancellationToken cancellationToken = default)
    {
        var target = Resolve(path);
        RefuseReadOnly(target);
        var temp = TempFor(target);
        try
        {
            await using (var stream = new FileStream(temp, Options(useAsync: true)))
            {
                whileTempIsOpen?.Invoke(temp);
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            Replace(temp, target);
        }
        finally
        {
            Discard(temp);
        }
    }

    // Owner only from the start: what the umask would leave to others is not given for the time of the write.
    private static FileStreamOptions Options(bool useAsync)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = useAsync ? FileOptions.Asynchronous : FileOptions.None,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    // The file a symbolic link points to (the link itself when it is not one, or when it leads nowhere yet).
    private static string Resolve(string path)
    {
        var info = new FileInfo(path);
        return (info.LinkTarget is not null ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName : null) ?? info.FullName;
    }

    // Replacing a file by a rename does not look at the file's own permissions, as writing into it does.
    private static void RefuseReadOnly(string target)
    {
        if (File.Exists(target) && new FileInfo(target).IsReadOnly)
            throw new UnauthorizedAccessException($"Access to the path '{target}' is denied: the file is read-only.");
    }

    private static string TempFor(string target) =>
        Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

    private static void Replace(string temp, string target)
    {
        // Only now, when the bytes are written, does the file get its permissions: the target's, or what a new file gets here.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, File.Exists(target) ? File.GetUnixFileMode(target) : DefaultMode(Path.GetDirectoryName(target)!));
        File.Move(temp, target, overwrite: true);
    }

    // The permissions a new file gets in the folder (the user's umask applied), found by making an empty one.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static UnixFileMode DefaultMode(string folder)
    {
        var probe = Path.Combine(folder, $".{Guid.NewGuid():N}.mode");
        try
        {
            File.WriteAllBytes(probe, []);
            return File.GetUnixFileMode(probe);
        }
        finally
        {
            Discard(probe);
        }
    }

    private static void Discard(string temp)
    {
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The write's own error is the one to report; a leftover hidden file is harmless.
        }
    }
}
