namespace Invoklet.Plugins.Hosting;

/// <summary>A plugin that loaded, with anything worth knowing about it (warnings only).</summary>
public sealed record LoadedPlugin(PluginManifest Manifest, string Directory, PluginTrust Trust, IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public string Id => Manifest.Id;
}

/// <summary>A plugin that didn't load and why. <paramref name="Manifest"/> is null when the manifest itself was the problem.</summary>
public sealed record RejectedPlugin(string Id, PluginManifest? Manifest, string Directory, PluginTrust Trust, IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>
/// What one start of Invoklet loaded. It is made once by <see cref="PluginLoader"/> and never changes: plugins are not loaded, unloaded
/// or rescanned while the application runs, so everything that lists plugins reads this and the Plugins page compares it with the
/// inventory on disk to show what waits for a restart.
/// </summary>
public sealed class PluginSession
{
    public static readonly PluginSession Empty = new([], []);
    private readonly PluginInventory _startupInventory;

    public PluginSession(IReadOnlyList<LoadedPlugin> loaded, IReadOnlyList<RejectedPlugin> rejected)
        : this(loaded, rejected, PluginInventory.Empty, []) { }

    internal PluginSession(IReadOnlyList<LoadedPlugin> loaded, IReadOnlyList<RejectedPlugin> rejected,
        PluginInventory startupInventory, IReadOnlyList<PluginDiagnostic> sourceDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(rejected);
        Loaded = Array.AsReadOnly(loaded.Select(p => p with { Diagnostics = Array.AsReadOnly(p.Diagnostics.ToArray()) }).ToArray());
        Rejected = Array.AsReadOnly(rejected.Select(p => p with { Diagnostics = Array.AsReadOnly(p.Diagnostics.ToArray()) }).ToArray());
        _startupInventory = startupInventory;
        Diagnostics = Array.AsReadOnly(Loaded.SelectMany(p => p.Diagnostics).Concat(Rejected.SelectMany(p => p.Diagnostics)).Concat(sourceDiagnostics).ToArray());
    }

    /// <summary>In the order they loaded, which is the order their roles registered.</summary>
    public IReadOnlyList<LoadedPlugin> Loaded { get; }

    public IReadOnlyList<RejectedPlugin> Rejected { get; }

    /// <summary>Every diagnostic of the start, loaded plugins' warnings first.</summary>
    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; }

    public LoadedPlugin? Find(string id) =>
        Loaded.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public bool IsLoaded(string id) => Find(id) is not null;

    /// <summary>Plugin ids whose desired inventory entry changed since this session started.</summary>
    public IReadOnlyList<string> GetPendingRestart(PluginInventory currentInventory)
    {
        ArgumentNullException.ThrowIfNull(currentInventory);
        if (currentInventory.ReadError is not null) throw new InvalidOperationException("The plugin inventory could not be read.");
        return _startupInventory.Entries.Select(entry => entry.Id).Union(currentInventory.Entries.Select(entry => entry.Id), StringComparer.Ordinal)
            .Where(id => !_startupInventory.SameEntry(id, currentInventory)).Order(StringComparer.Ordinal).ToArray();
    }
}
