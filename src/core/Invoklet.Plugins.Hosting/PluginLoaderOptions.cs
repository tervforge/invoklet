namespace Invoklet.Plugins.Hosting;

/// <summary>What the loader needs to know about the application hosting the plugins.</summary>
public sealed class PluginLoaderOptions
{
    /// <summary>
    /// The assemblies every plugin shares with the application instead of loading its own copy: the ones whose types cross the
    /// boundary. A plugin that ships one of these gets the application's anyway, or its <c>IInvokletPlugin</c> would be a different
    /// type from the host's. Framework assemblies (<c>System.*</c>) are always shared.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSharedAssemblies =
    [
        "Invoklet.Plugins",
        "Invoklet.Contracts",
        "Invoklet.Format.MdRun",
        "Microsoft.Extensions.Logging.Abstractions",
    ];

    /// <summary>The version of this Invoklet, checked against each manifest's <c>minAppVersion</c>.</summary>
    public required Version ApplicationVersion { get; init; }

    /// <summary>See <see cref="DefaultSharedAssemblies"/>; the application adds what its contracts expose.</summary>
    public IReadOnlyList<string> SharedAssemblies { get; init; } = DefaultSharedAssemblies;

    public string? BundledDirectory { get; init; }
    public BundledPluginCatalog BundledCatalog { get; init; } = BundledPluginCatalog.Empty;

    public string? UserDirectory { get; init; }
    public PluginInventory Inventory { get; init; } = PluginInventory.Empty;

    /// <summary>An explicitly opted-in plugin directory or directory of plugins.</summary>
    public string? DevelopmentDirectory { get; init; }

    /// <summary>
    /// Where verified copies of plugins' native libraries are kept, one folder per plugin content hash, reused across starts and
    /// pruned of plugins that are gone. The host puts it in its machine-local data, not in a temporary folder: a temporary folder
    /// may forbid executing from it, and nothing would clean it.
    /// </summary>
    public required string NativeLibraryCacheDirectory { get; init; }
}
