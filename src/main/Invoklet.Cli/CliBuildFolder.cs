using System.Security.Cryptography;
using System.Text;

namespace Invoklet.Cli;

/// <summary>
/// The folder a script run from the command line is built in. It is kept between runs, one per script, as the window keeps its own, so an
/// unchanged script, its packages and its connections' contract are not built again. A run holds the folder's lock; a second run of the
/// same script at the same time builds in a temporary folder instead, removed when it ends.
/// </summary>
internal sealed class CliBuildFolder : IDisposable
{
    private readonly FileStream? _lock;

    private CliBuildFolder(string path, FileStream? lockFile)
    {
        Path = path;
        _lock = lockFile;
    }

    public string Path { get; }

    /// <summary>The folder of the script under <paramref name="root"/>, or a temporary one when <paramref name="root"/> is null or the folder is in use.</summary>
    public static CliBuildFolder Open(string? root, string scriptPath)
    {
        if (root is not null)
        {
            var folder = System.IO.Path.Combine(root, KeyOf(scriptPath));
            try
            {
                Directory.CreateDirectory(root);
                var lockFile = new FileStream(folder + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Directory.CreateDirectory(folder);
                // What PruneStale goes by.
                Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow);
                return new CliBuildFolder(folder, lockFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return new CliBuildFolder(Directory.CreateTempSubdirectory("Invoklet-cli-").FullName, null);
    }

    /// <summary>Deletes the folders under <paramref name="root"/> no run has used for <paramref name="maxAge"/>, but those a run holds now.</summary>
    public static void PruneStale(string root, TimeSpan maxAge)
    {
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(folder) >= cutoff) continue;
                using (new FileStream(folder + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    Directory.Delete(folder, recursive: true);
                File.Delete(folder + ".lock");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Paths that name the same file on this system name the same folder.
    private static string KeyOf(string scriptPath)
    {
        var full = System.IO.Path.GetFullPath(scriptPath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) full = full.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32].ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_lock is not null)
        {
            _lock.Dispose();
            return;
        }
        try { Directory.Delete(Path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a locked file here isn't worth failing the run over */ }
    }
}
