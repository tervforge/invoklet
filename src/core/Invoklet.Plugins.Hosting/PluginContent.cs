using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Invoklet.Plugins.Hosting;

/// <summary>A deterministic SHA-256 fingerprint of the plugin's relative file names and contents.</summary>
public static class PluginContent
{
    public static string ComputeHash(string directory) => PluginFiles.Read(directory).ContentHash;
}

internal sealed class PluginFiles(string root, Dictionary<string, byte[]> digests, string contentHash)
{
    private readonly Lazy<string> _nativeSnapshot = new(() => CreateNativeSnapshot(root, digests));

    internal string ContentHash { get; } = contentHash;

    internal static PluginFiles Read(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var digests = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        Collect(root);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var (relativePath, digest) in digests.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(relativePath);
            BinaryPrimitives.WriteInt32LittleEndian(length, name.Length);
            hash.AppendData(length);
            hash.AppendData(name);
            hash.AppendData(digest);
        }
        return new PluginFiles(root, digests, Convert.ToHexStringLower(hash.GetHashAndReset()));

        void Collect(string folder)
        {
            RejectLink(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = RejectLink(path);
                if ((attributes & FileAttributes.Directory) != 0) Collect(path);
                else
                {
                    using var file = File.OpenRead(path);
                    digests.Add(RelativeName(root, path), SHA256.HashData(file));
                }
            }
        }
    }

    internal MemoryStream ReadVerified(string path)
    {
        var expected = GetExpectedDigest(path);
        using var file = File.OpenRead(path);
        var contents = new MemoryStream();
        try
        {
            file.CopyTo(contents);
            if (!SHA256.HashData(contents.GetBuffer().AsSpan(0, checked((int)contents.Length))).AsSpan().SequenceEqual(expected))
                throw new IOException($"Plugin file changed after verification: {path}");
            contents.Position = 0;
            return contents;
        }
        catch
        {
            contents.Dispose();
            throw;
        }
    }

    // The resolver reads the dependency manifest by path. Actual loads still check the chosen file against the approved set.
    internal FileStream OpenVerified(string path)
    {
        var expected = GetExpectedDigest(path);
        var file = File.OpenRead(path);
        try
        {
            if (!SHA256.HashData(file).AsSpan().SequenceEqual(expected))
                throw new IOException($"Plugin file changed after verification: {path}");
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    internal FileStream? OpenResolverManifest(string path)
    {
        if (!digests.ContainsKey(RelativeName(root, path)) && !File.Exists(path)) return null;
        return OpenVerified(path);
    }

    internal string GetNativeLoadPath(string path)
    {
        GetExpectedDigest(path);
        return Path.Combine(_nativeSnapshot.Value, RelativeName(root, path));
    }

    private static string CreateNativeSnapshot(string root, Dictionary<string, byte[]> digests)
    {
        // A verified open handle does not bind a later native path lookup to that handle on Unix.
        // Keep the original relative layout for libraries that resolve neighbours via $ORIGIN or @loader_path.
        var snapshot = Directory.CreateTempSubdirectory("Invoklet-plugin-native-").FullName;
        try
        {
            foreach (var (relativePath, expected) in digests)
            {
                var sourcePath = Path.Combine(root, relativePath);
                RejectLinksBetween(root, sourcePath);
                var destination = Path.Combine(snapshot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = File.OpenRead(sourcePath);
                using var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                source.CopyTo(copy);
                copy.Position = 0;
                if (!SHA256.HashData(copy).AsSpan().SequenceEqual(expected))
                    throw new IOException($"Plugin file changed before native loading: {sourcePath}");
            }
        }
        catch
        {
            DeleteNativeSnapshot(snapshot);
            throw;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteNativeSnapshot(snapshot);
        return snapshot;
    }

    private static void DeleteNativeSnapshot(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The native module may still be mapped during process exit on Windows.
        }
    }

    private byte[] GetExpectedDigest(string path)
    {
        if (!digests.TryGetValue(RelativeName(root, path), out var expected))
            throw new IOException($"Path is outside the approved plugin files: {path}");
        RejectLinksBetween(root, path);
        return expected;
    }

    private static string RelativeName(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    internal static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    internal static void RejectLinksBetween(string root, string path)
    {
        RejectLink(root);
        var current = root;
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".") return;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
    }

    private static FileAttributes RejectLink(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new IOException($"Plugin contents include a link or a device: {path}");
        return attributes;
    }
}
