using System.IO.Compression;

namespace Invoklet.Plugins.Hosting;

/// <summary>Prepares a copy for review before it can enter the user's plugin inventory.</summary>
public sealed class PluginPackageInstaller(string userDirectory, PluginInventoryStore inventory, BundledPluginCatalog bundled)
{
    public const string InstalledSource = "preferences";
    private const long MaxArchiveBytes = 64L * 1024 * 1024;

    public PreparedPluginPackage PrepareArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 2048 || archive.Entries.Sum(entry => (decimal)entry.Length) > MaxArchiveBytes
            || archive.Entries.Any(entry => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000))
            throw new InvalidDataException("The archive exceeds the plugin package limits or contains a link.");
        var staging = CreateStaging();
        var isNuGetPackage = string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase);
        var keepStaging = false;
        try
        {
            ExtractArchive(archive, staging);
            if (isNuGetPackage)
            {
                var payload = Path.Combine(staging, "plugin");
                if (!Directory.Exists(payload))
                    throw new InvalidDataException("The NuGet package has no plugin folder.");
                return Prepare(payload);
            }
            var package = ReadPreparedPackage(staging, PluginContent.ComputeHash(staging));
            keepStaging = true;
            return package;
        }
        finally
        {
            if (!keepStaging) Directory.Delete(staging, recursive: true);
        }
    }

    private static void ExtractArchive(ZipArchive archive, string staging)
    {
        var remaining = MaxArchiveBytes;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var directory = name.EndsWith('/');
            var relative = directory ? name[..^1] : name;
            if (relative.Split('/').Any(part => part.Length == 0 || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ')))
                throw new IOException("The archive contains an invalid plugin path.");
            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!PluginFiles.IsInside(staging, destination))
                throw new IOException("The archive entry is outside the plugin package.");
            if (directory)
            {
                if (entry.Length != 0) throw new InvalidDataException("An archive directory contains data.");
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long written = 0;
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                remaining -= count;
                written += count;
                if (remaining < 0 || written > entry.Length)
                    throw new InvalidDataException("The archive exceeds its declared size or the plugin package limit.");
                output.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("An archive entry has an incorrect size.");
        }
    }

    public PreparedPluginPackage Prepare(string source)
    {
        // Hashing also rejects links throughout the source tree.
        var sourceHash = PluginContent.ComputeHash(source);
        var staging = CreateStaging();
        try
        {
            CopyDirectory(source, staging);
            if (PluginContent.ComputeHash(staging) != sourceHash)
                throw new IOException("The package changed while it was being copied.");
            return ReadPreparedPackage(staging, sourceHash);
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private PreparedPluginPackage ReadPreparedPackage(string staging, string hash)
    {
        var manifest = PluginDiscovery.ReadFolder(staging, new PluginSource(staging, PluginTrust.Trusted)).Manifest;
        if (manifest is null || bundled.ReadError is not null || bundled.Find(manifest.Id) is not null)
            throw new InvalidOperationException("Choose a valid plugin package with a unique ID.");
        if (manifest.Roles.Count != 1 || (!manifest.HasRole(PluginRole.Theme) && !manifest.HasRole(PluginRole.Connection)))
            throw new InvalidOperationException("Choose a theme or connector package.");
        if (manifest.HasRole(PluginRole.Theme) && manifest.Entry is not null)
            throw new InvalidOperationException("A theme package cannot contain an entry point.");
        if (manifest.HasRole(PluginRole.Connection) && (manifest.Entry is not { } entry
            || !File.Exists(Path.Combine(staging, entry.Assembly))))
            throw new InvalidOperationException("The connector entry assembly is missing.");
        return new PreparedPluginPackage(manifest, staging, hash);
    }

    public void Install(PreparedPluginPackage package)
    {
        var manifest = package.Manifest;
        if (bundled.ReadError is not null || bundled.Find(manifest.Id) is not null)
            throw new InvalidOperationException("This ID is reserved for a bundled plugin.");
        var parent = Path.Combine(userDirectory, manifest.Id);
        var destination = Path.Combine(parent, manifest.Version);
        if (!PluginFiles.IsInside(userDirectory, destination))
            throw new InvalidOperationException("The package destination is invalid.");

        var moved = false;
        inventory.Update(current =>
        {
            if (current.Find(manifest.Id) is { Source: not PluginInventoryStore.BundledSource } installed &&
                (installed.Source != InstalledSource || installed.Version != manifest.Version))
                throw new InvalidOperationException("Remove the existing plugin version before installing another.");

            Directory.CreateDirectory(userDirectory);
            PluginFiles.RejectLinksBetween(userDirectory, userDirectory);
            Directory.CreateDirectory(parent);
            PluginFiles.RejectLinksBetween(userDirectory, parent);
            if (File.Exists(Path.Combine(parent, PluginContracts.ManifestFileName)))
                throw new InvalidOperationException("A manually installed package already uses this ID.");
            if (PluginContent.ComputeHash(package.Directory) != package.ContentHash)
                throw new IOException("The prepared package changed after review.");
            if (Directory.Exists(destination))
            {
                PluginFiles.RejectLinksBetween(userDirectory, destination);
                if (PluginContent.ComputeHash(destination) != package.ContentHash)
                    throw new InvalidOperationException("A different package already uses this ID and version.");
            }
            else
            {
                Directory.Move(package.Directory, destination);
                moved = true;
            }
            return current.Entries.Where(entry => entry.Id != manifest.Id)
                .Append(new PluginInventoryEntry(manifest.Id, manifest.Version, manifest.Roles.Keys.Select(PluginRoles.Name).ToArray(), InstalledSource,
                    manifest.Publisher, package.ContentHash, "none", Enabled: true, Approved: true));
        }, () =>
        {
            if (moved) Directory.Move(destination, package.Directory);
        });
    }

    public void Remove(string id)
    {
        if (bundled.ReadError is not null || bundled.Find(id) is not null)
            throw new InvalidOperationException("The plugin is not installed in the user inventory.");

        var staging = CreateStaging();
        var removed = Path.Combine(staging, "package");
        string? original = null;
        var committed = false;
        try
        {
            inventory.Update(current =>
            {
                var entry = current.Find(id);
                if (!IsManaged(entry))
                    throw new InvalidOperationException("The plugin is not installed in the user inventory.");

                var destination = Path.Combine(userDirectory, id, entry.Version);
                if (Directory.Exists(destination))
                {
                    if (!PluginFiles.IsInside(userDirectory, destination))
                        throw new InvalidOperationException("The package destination is invalid.");
                    PluginFiles.RejectLinksBetween(userDirectory, destination);
                    Directory.Move(destination, removed);
                    original = destination;
                }
                return current.Entries.Where(item => item.Id != id);
            }, () =>
            {
                if (original is not null) Directory.Move(removed, original);
            });
            committed = true;
        }
        finally
        {
            // Keep the staged copy if restoring the package failed.
            if (committed || !Directory.Exists(removed))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceWarning("Could not clean up removed plugin staging directory: {0}", ex.Message);
                }
            }
        }
    }

    public static bool IsManaged([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] PluginInventoryEntry? entry) =>
        entry is { Approved: true, Source: InstalledSource, Roles: ["theme" or "connection"] };

    private string CreateStaging()
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(userDirectory))!;
        Directory.CreateDirectory(parent);
        return Directory.CreateDirectory(Path.Combine(parent, ".plugin-install-" + Guid.NewGuid().ToString("N"))).FullName;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            PluginFiles.RejectLinksBetween(source, path);
            var target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                Directory.CreateDirectory(target);
                CopyDirectory(path, target);
            }
            else File.Copy(path, target);
        }
    }
}

public sealed class PreparedPluginPackage : IDisposable
{
    internal PreparedPluginPackage(PluginManifest manifest, string directory, string contentHash)
    {
        Manifest = manifest;
        Directory = directory;
        ContentHash = contentHash;
    }

    public PluginManifest Manifest { get; }
    internal string Directory { get; }
    internal string ContentHash { get; }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
    }
}
