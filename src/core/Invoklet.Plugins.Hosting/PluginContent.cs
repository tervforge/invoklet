using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Invoklet.Plugins.Hosting;

/// <summary>A deterministic SHA-256 fingerprint of the plugin's relative file names and contents.</summary>
public static class PluginContent
{
    public static string ComputeHash(string directory) => PluginFiles.Read(directory, nativeCacheDirectory: null).ContentHash;
}

/// <summary>
/// The approved files of one plugin: their digests as of admission, and the reads that check a file against them before it is used.
/// Native libraries are loaded from a copy kept under a cache folder named by the plugin's content hash, so a copy made by an earlier
/// start is reused while its files still match, and the copies of plugins that are gone can be removed at the next start.
/// </summary>
internal sealed class PluginFiles(string root, Dictionary<string, byte[]> digests, string contentHash, string? nativeCacheDirectory)
{
    private static readonly ConcurrentBag<FileStream> NativeLeases = [];
    private readonly Lazy<string> _nativeSnapshot = new(() => nativeCacheDirectory is null
        ? throw new InvalidOperationException("No native library cache directory was given for this plugin.")
        : OpenNativeSnapshot(root, digests, nativeCacheDirectory, contentHash));

    internal string ContentHash { get; } = contentHash;

    internal static PluginFiles Read(string directory, string? nativeCacheDirectory)
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
        return new PluginFiles(root, digests, Convert.ToHexStringLower(hash.GetHashAndReset()), nativeCacheDirectory);

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

    /// <summary>Whether the path is one of the approved files.</summary>
    internal bool Contains(string path) => digests.ContainsKey(RelativeName(root, path));

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
        if (!Contains(path) && !File.Exists(path)) return null;
        return OpenVerified(path);
    }

    internal string GetNativeLoadPath(string path)
    {
        GetExpectedDigest(path);
        return Path.Combine(_nativeSnapshot.Value, RelativeName(root, path));
    }

    /// <summary>
    /// Removes the native copies under the cache that belong to none of the content hashes given: plugins removed, updated or no
    /// longer approved. A shared lease protects files still needed by another process, including lazy native dependencies.
    /// </summary>
    internal static void PruneNativeCache(string cacheDirectory, IReadOnlySet<string> keepContentHashes)
    {
        string[] folders;
        try
        {
            RejectLink(cacheDirectory);
            folders = Directory.GetDirectories(cacheDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var hash = name.Split('.', 2)[0];
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) continue;
            if (name == hash && keepContentHashes.Contains(hash)) continue;
            if (name != hash && !name.EndsWith(TemporarySuffix, StringComparison.Ordinal)) continue;
            // A copy being made by another start right now has a temporary name; leave it alone for a while.
            if (name.EndsWith(TemporarySuffix, StringComparison.Ordinal) && IsRecent(folder)) continue;
            using var lease = TryOpenExclusiveLease(cacheDirectory, hash);
            if (lease is not null) DeleteNativeSnapshot(folder);
        }
    }

    private const string TemporarySuffix = ".tmp";

    private static bool IsRecent(string folder)
    {
        try { return DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) < TimeSpan.FromHours(1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // One folder per content hash under the cache. An existing folder is reused when every approved file in it still matches;
    // otherwise a fresh copy is made under a temporary name, checked, and moved into place. Two starts making the same copy at once
    // both end with a matching folder: the one that loses the move keeps using its own copy.
    private static string OpenNativeSnapshot(string root, Dictionary<string, byte[]> digests, string cacheDirectory, string contentHash)
    {
        Directory.CreateDirectory(cacheDirectory);
        RejectLink(cacheDirectory);
        var snapshot = Path.Combine(cacheDirectory, contentHash);
        var lease = OpenSharedLease(cacheDirectory, contentHash);
        try
        {
            if (!Matches(snapshot, contentHash))
            {
                lease.Dispose();
                using (var exclusive = TryOpenExclusiveLease(cacheDirectory, contentHash))
                {
                    if (exclusive is not null && Directory.Exists(snapshot) && !Matches(snapshot, contentHash))
                        DeleteNativeSnapshot(snapshot);
                }
                lease = OpenSharedLease(cacheDirectory, contentHash);
                if (!Matches(snapshot, contentHash))
                    snapshot = PublishNativeSnapshot(root, digests, cacheDirectory, contentHash, snapshot);
            }
            // Native modules may resolve neighbours until the process terminates.
            NativeLeases.Add(lease);
            return snapshot;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static string PublishNativeSnapshot(string root, Dictionary<string, byte[]> digests, string cacheDirectory, string contentHash, string snapshot)
    {
        var temporary = Path.Combine(cacheDirectory, $"{contentHash}.{Guid.NewGuid():N}{TemporarySuffix}");
        try
        {
            CopyVerified(root, digests, temporary);
        }
        catch
        {
            DeleteNativeSnapshot(temporary);
            throw;
        }

        if (Directory.Exists(snapshot))
        {
            if (Matches(snapshot, contentHash))
            {
                DeleteNativeSnapshot(temporary);
                return snapshot;
            }
            return temporary;
        }
        try
        {
            Directory.Move(temporary, snapshot);
        }
        catch (IOException) when (Directory.Exists(snapshot))
        {
            if (!Matches(snapshot, contentHash)) return temporary;
            DeleteNativeSnapshot(temporary);
        }
        return snapshot;
    }

    private static void CopyVerified(string root, Dictionary<string, byte[]> digests, string destinationRoot)
    {
        // A verified open handle does not bind a later native path lookup to that handle on Unix.
        // Keep the original relative layout for libraries that resolve neighbours via $ORIGIN or @loader_path.
        Directory.CreateDirectory(destinationRoot);
        foreach (var (relativePath, expected) in digests)
        {
            var sourcePath = Path.Combine(root, relativePath);
            RejectLinksBetween(root, sourcePath);
            var destination = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = File.OpenRead(sourcePath);
            using var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            source.CopyTo(copy);
            copy.Position = 0;
            if (!SHA256.HashData(copy).AsSpan().SequenceEqual(expected))
                throw new IOException($"Plugin file changed before native loading: {sourcePath}");
        }
    }

    private static bool Matches(string snapshot, string contentHash)
    {
        try
        {
            return Read(snapshot, nativeCacheDirectory: null).ContentHash == contentHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static FileStream OpenSharedLease(string cacheDirectory, string hash)
    {
        var path = Path.Combine(cacheDirectory, hash + ".lock");
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read); }
            catch (IOException) when (waited.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(25); }
        }
    }

    private static FileStream? TryOpenExclusiveLease(string cacheDirectory, string hash)
    {
        try { return new FileStream(Path.Combine(cacheDirectory, hash + ".lock"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void DeleteNativeSnapshot(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The native module may still be mapped by this or another process on Windows.
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
