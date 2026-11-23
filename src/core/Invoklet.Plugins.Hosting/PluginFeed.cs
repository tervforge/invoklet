using System.IO.Compression;

namespace Invoklet.Plugins.Hosting;

/// <summary>A plugin package a feed lists: what can be shown before anything is downloaded.</summary>
public sealed record PluginFeedPackage(string Id, string Title, string Description, string LatestVersion);

public enum PluginFeedError
{
    /// <summary>The feed could not be reached or answered with something that is not a NuGet feed.</summary>
    Unreachable,

    /// <summary>The feed refused the request: it needs credentials, or the ones it got are not accepted.</summary>
    Unauthorized,
}

/// <summary>A feed failure whose message is safe to show: it names no address, credential or local path.</summary>
public sealed class PluginFeedException(PluginFeedError error, Exception? innerException = null)
    : Exception(error == PluginFeedError.Unauthorized
        ? "The plugin feed refused access. It needs credentials, or the ones configured are not accepted."
        : "The plugin feed could not be reached. Check the connection and try again.", innerException)
{
    public PluginFeedError Error { get; } = error;
}

/// <summary>A NuGet feed of plugin packages. Nothing it returns is run: packages are only listed and downloaded.</summary>
public interface IPluginFeed
{
    /// <summary>The plugin packages the feed lists.</summary>
    /// <exception cref="PluginFeedException">The feed failed.</exception>
    Task<IReadOnlyList<PluginFeedPackage>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The versions of a package, the one to prefer first: stable versions newest first, then pre-releases newest first.</summary>
    /// <exception cref="PluginFeedException">The feed failed.</exception>
    Task<IReadOnlyList<string>> GetVersionsAsync(string id, CancellationToken cancellationToken);

    /// <summary>The bytes of one version's <c>.nupkg</c>.</summary>
    /// <exception cref="PluginFeedException">The feed failed.</exception>
    Task<byte[]> DownloadAsync(string id, string version, CancellationToken cancellationToken);
}

/// <summary>The version of a feed package chosen for this Invoklet, with the package itself.</summary>
/// <param name="Skipped">Why each newer version was passed over, newest first.</param>
public sealed record PluginFeedChoice(string Id, string Version, byte[] Package, IReadOnlyList<string> Skipped);

/// <summary>Chooses the newest version of a feed package that this Invoklet can install, by reading each candidate's manifest.</summary>
public sealed class PluginFeedResolver(IPluginFeed feed, Version applicationVersion, BundledPluginCatalog bundled)
{
    /// <summary>How many versions are downloaded at most before giving up: old versions are rarely the answer.</summary>
    public const int MaxCandidates = 10;

    private const long MaxManifestBytes = 1024 * 1024;

    /// <summary>
    /// Whether a package id names the plugin: the plugin id itself, or the plugin id under a publisher prefix, such as
    /// <c>tervforge.invoklet.connector.postgres</c> for <c>invoklet.connector.postgres</c>.
    /// </summary>
    public static bool Names(string packageId, string pluginId) =>
        packageId.Length >= pluginId.Length && packageId.EndsWith(pluginId, StringComparison.OrdinalIgnoreCase)
        && (packageId.Length == pluginId.Length || packageId[packageId.Length - pluginId.Length - 1] == '.');

    /// <summary>The listed package of a plugin: the one with the plugin's own id first, then the shortest prefixed one.</summary>
    public static PluginFeedPackage? Find(IEnumerable<PluginFeedPackage> packages, string pluginId) =>
        packages.Where(package => Names(package.Id, pluginId)).OrderBy(package => package.Id.Length).FirstOrDefault();

    /// <summary>The plugin packages the feed lists.</summary>
    /// <exception cref="PluginFeedException">The feed failed.</exception>
    public Task<IReadOnlyList<PluginFeedPackage>> ListAsync(CancellationToken cancellationToken) => feed.ListAsync(cancellationToken);

    /// <summary>The newest compatible version, or null with the reasons in <paramref name="skipped"/> when none of the candidates fits.</summary>
    /// <exception cref="PluginFeedException">The feed failed.</exception>
    public async Task<PluginFeedChoice?> ChooseAsync(string id, List<string> skipped, CancellationToken cancellationToken)
    {
        if (bundled.Find(id) is not null)
        {
            skipped.Add($"'{id}' is included with Invoklet and is updated with it.");
            return null;
        }

        foreach (var version in (await feed.GetVersionsAsync(id, cancellationToken)).Take(MaxCandidates))
        {
            var package = await feed.DownloadAsync(id, version, cancellationToken);
            var manifest = ReadManifest(package);
            // A prefixed package id can only be matched with the bundled plugins once its manifest names the plugin.
            if (manifest is not null && Names(id, manifest.Id) && bundled.Find(manifest.Id) is not null)
            {
                skipped.Add($"'{manifest.Id}' is included with Invoklet and is updated with it.");
                return null;
            }
            var reason = Check(manifest, id, version);
            if (reason is null) return new PluginFeedChoice(id, version, package, skipped.ToArray());
            skipped.Add($"{version}: {reason}");
        }
        if (skipped.Count == 0) skipped.Add($"The feed has no versions of '{id}'.");
        return null;
    }

    /// <summary>Why a package's manifest can't be installed here; null when it can.</summary>
    internal string? Check(PluginManifest? manifest, string id, string version)
    {
        if (manifest is null) return "the package has no valid plugin/invoklet-plugin.json.";
        if (!Names(id, manifest.Id)
            || !string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase))
            return "the manifest names a different package or version.";
        if (manifest.MinAppVersion is { } minimum && minimum > applicationVersion)
            return $"needs Invoklet {minimum} or newer.";
        // The roles PluginPackageInstaller accepts.
        if (manifest.Roles.Count != 1 || !(manifest.HasRole(PluginRole.Theme) || manifest.HasRole(PluginRole.Connection)
            || manifest.HasRole(PluginRole.Library)))
            return "only theme, connector and library packages can be installed.";
        foreach (var (role, contract) in manifest.Roles)
            if (!PluginContracts.SupportedVersions(role).Contains(contract))
                return $"written for {PluginRoles.Name(role)} contract {contract}, which this Invoklet does not support.";
        return null;
    }

    /// <summary>The manifest of a <c>.nupkg</c>, read from the archive without extracting anything; null when it is missing or invalid.</summary>
    internal static PluginManifest? ReadManifest(byte[] package)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(item.FullName.Replace('\\', '/'), "plugin/" + PluginContracts.ManifestFileName, StringComparison.Ordinal));
            if (entry is null || entry.Length > MaxManifestBytes) return null;
            // The declared length can lie; reading stops at the limit either way.
            using var reader = new StreamReader(entry.Open());
            var text = new char[MaxManifestBytes];
            var length = reader.ReadBlock(text, 0, text.Length);
            return reader.Peek() >= 0 ? null : PluginManifestReader.Read(new string(text, 0, length)).Manifest;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }
}
