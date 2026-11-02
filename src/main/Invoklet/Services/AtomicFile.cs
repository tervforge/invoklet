namespace Invoklet.Services;

/// <summary>
/// Writes a file so that a write that fails leaves the file as it was: the bytes go to a temporary file in the same folder, which then
/// replaces the target. The target keeps its permission bits (macOS and Linux) and, when it is a symbolic link, stays one: the file it
/// points to is replaced. A failure (disk full, a lost network share, no permission) throws IOException or UnauthorizedAccessException.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var target = Resolve(path);
        RefuseReadOnly(target);
        var temp = TempFor(target);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
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

    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var target = Resolve(path);
        RefuseReadOnly(target);
        var temp = TempFor(target);
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
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
        if (!OperatingSystem.IsWindows() && File.Exists(target)) File.SetUnixFileMode(temp, File.GetUnixFileMode(target));
        File.Move(temp, target, overwrite: true);
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
