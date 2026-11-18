using System.Net;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Invoklet.BuiltIns;

/// <summary>A plugin feed read with the NuGet v3 protocol: search to list, the package base address for versions and downloads.</summary>
public sealed class NuGetPluginFeed(PackageSource source) : IPluginFeed
{
    private const int MaxListed = 200;
    private const long MaxPackageBytes = 256L * 1024 * 1024;
    private readonly SourceRepository _repository = Repository.Factory.GetCoreV3(source);

    public NuGetPluginFeed(string url) : this(new PackageSource(url, "Invoklet plugins")) { }

    public Task<IReadOnlyList<PluginFeedPackage>> ListAsync(CancellationToken cancellationToken) => Run<IReadOnlyList<PluginFeedPackage>>(async () =>
    {
        var search = await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken)
            ?? throw new PluginFeedException(PluginFeedError.Unreachable);
        var results = await search.SearchAsync("invoklet", new SearchFilter(includePrerelease: false), 0, MaxListed,
            NullLogger.Instance, cancellationToken);
        // The feed may hold other packages too (the contracts plugins compile against); only plugin packages are listed.
        return results
            .Where(item => PluginManifestReader.IsValidId(item.Identity.Id)
                && (item.Tags ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Contains("plugin", StringComparer.OrdinalIgnoreCase))
            .Select(item => new PluginFeedPackage(item.Identity.Id, Clean(item.Title) ?? item.Identity.Id, Clean(item.Description) ?? "",
                item.Identity.Version.ToNormalizedString()))
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    });

    public Task<IReadOnlyList<string>> GetVersionsAsync(string id, CancellationToken cancellationToken) => Run<IReadOnlyList<string>>(async () =>
    {
        using var cache = NoCache();
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken)
            ?? throw new PluginFeedException(PluginFeedError.Unreachable);
        var versions = await resource.GetAllVersionsAsync(id, cache, NullLogger.Instance, cancellationToken);
        return versions.OrderBy(version => version.IsPrerelease).ThenByDescending(version => version, VersionComparer.Default)
            .Select(version => version.ToNormalizedString()).ToArray();
    });

    public Task<byte[]> DownloadAsync(string id, string version, CancellationToken cancellationToken) => Run(async () =>
    {
        using var cache = NoCache();
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken)
            ?? throw new PluginFeedException(PluginFeedError.Unreachable);
        using var package = new MemoryStream();
        if (!await resource.CopyNupkgToStreamAsync(id, NuGetVersion.Parse(version), package, cache, NullLogger.Instance, cancellationToken))
            throw new PluginFeedException(PluginFeedError.Unreachable);
        // shortcut: the size is checked after the download is in memory; stream to a bounded file if feeds start serving huge packages.
        if (package.Length > MaxPackageBytes) throw new PluginFeedException(PluginFeedError.Unreachable);
        return package.ToArray();
    });

    // Nothing from the plugin feed is kept in NuGet's shared HTTP cache.
    private static SourceCacheContext NoCache() => new() { NoCache = true, DirectDownload = true };

    private static async Task<T> Run<T>(Func<Task<T>> query)
    {
        try
        {
            return await query();
        }
        catch (Exception ex) when (ex is not (PluginFeedException or OperationCanceledException))
        {
            throw new PluginFeedException(IsUnauthorized(ex) ? PluginFeedError.Unauthorized : PluginFeedError.Unreachable, ex);
        }
    }

    // NuGet reports a refused request as a protocol exception whose message carries the status code.
    private static bool IsUnauthorized(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                || current.Message.Contains("401", StringComparison.Ordinal) || current.Message.Contains("403", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string? Clean(string? text)
    {
        if (text is null) return null;
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned.Length > 300 ? cleaned[..300] + "…" : cleaned;
    }
}
