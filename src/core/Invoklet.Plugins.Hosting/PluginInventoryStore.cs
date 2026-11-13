namespace Invoklet.Plugins.Hosting;

/// <summary>Updates the desired startup state without changing the current plugin session.</summary>
public sealed class PluginInventoryStore(string path)
{
    public const string BundledSource = "bundled";

    public PluginInventory Read() => PluginInventory.Read(path);

    public PluginInventory Read(BundledPluginCatalog bundled, IReadOnlyDictionary<string, string> replacedThemeIds) =>
        WithReplacedThemes(Read(), bundled, replacedThemeIds);

    public void SetEnabled(string id, bool enabled, BundledPluginCatalog bundled,
        IReadOnlyDictionary<string, string>? replacedThemeIds = null, IReadOnlyList<string>? bundledRoles = null,
        PluginSession? session = null, string? userDirectory = null)
    {
        if (bundled.ReadError is not null) throw new InvalidOperationException("The bundled plugin catalogue could not be read.");
        Update(current =>
        {
            if (replacedThemeIds is not null)
                current = WithReplacedThemes(current, bundled, replacedThemeIds);
            var sessionManifests = session is null ? [] : session.Loaded.Select(plugin => (plugin.Manifest, plugin.Trust))
                .Concat(session.Rejected.Where(plugin => plugin.Manifest is not null)
                    .Select(plugin => (Manifest: plugin.Manifest!, plugin.Trust)))
                .Where(plugin => plugin.Trust == PluginTrust.Bundled && bundled.Find(plugin.Manifest.Id) is not null
                    || plugin.Trust == PluginTrust.Trusted && bundled.Find(plugin.Manifest.Id) is null
                        && current.Find(plugin.Manifest.Id) is { Approved: true, Source: not BundledSource } entry
                        && entry.Version == plugin.Manifest.Version)
                .Select(plugin => plugin.Manifest).ToArray();
            var manifests = sessionManifests.Concat(ReadPendingPackages(current, sessionManifests, userDirectory))
                .DistinctBy(manifest => manifest.Id, StringComparer.Ordinal).ToArray();
            var entries = current.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            if (enabled)
            {
                var manifest = manifests.FirstOrDefault(manifest => manifest.Id == id);
                var disabled = manifest?.DependsOn.FirstOrDefault(dependency =>
                    entries.GetValueOrDefault(dependency) is { Enabled: false });
                if (disabled is not null)
                    throw new PluginRegistrationException($"Enable the dependency '{disabled}' before enabling this plugin.");
            }

            SetState(id, enabled, bundledRoles);
            if (!enabled)
            {
                var dependants = manifests.SelectMany(manifest => manifest.DependsOn.Select(dependency => (dependency, manifest)))
                    .ToLookup(item => item.dependency, item => item.manifest, StringComparer.Ordinal);
                var visited = new HashSet<string>(StringComparer.Ordinal) { id };
                var queue = new Queue<string>();
                queue.Enqueue(id);
                while (queue.TryDequeue(out var dependency))
                {
                    foreach (var manifest in dependants[dependency])
                    {
                        if (!visited.Add(manifest.Id)) continue;
                        SetState(manifest.Id, false, manifest.Roles.Keys.Select(PluginRoles.Name).ToArray());
                        queue.Enqueue(manifest.Id);
                    }
                }
            }
            return entries.Values;

            void SetState(string pluginId, bool value, IReadOnlyList<string>? roles)
            {
                if (bundled.Find(pluginId) is { } package)
                {
                    if (value) entries.Remove(pluginId);
                    else entries[pluginId] = DisabledPackage(package, roles ?? ["theme"]);
                }
                else if (entries.GetValueOrDefault(pluginId) is { Approved: true, Source: not BundledSource } entry)
                    entries[pluginId] = entry with { Enabled = value };
                else
                    throw new InvalidOperationException("The plugin has no approved inventory entry.");
            }
        });
    }

    private static IEnumerable<PluginManifest> ReadPendingPackages(PluginInventory inventory,
        IReadOnlyList<PluginManifest> known, string? userDirectory)
    {
        if (userDirectory is null) yield break;
        var knownIds = known.Select(manifest => manifest.Id).ToHashSet(StringComparer.Ordinal);
        var source = new PluginSource(userDirectory, PluginTrust.Trusted);
        foreach (var entry in inventory.Entries.Where(entry => PluginPackageInstaller.IsManaged(entry) && !knownIds.Contains(entry.Id)))
        {
            var directory = Path.Combine(userDirectory, entry.Id, entry.Version);
            PluginManifest? manifest;
            try
            {
                PluginFiles.RejectLinksBetween(userDirectory, directory);
                if (!string.Equals(PluginContent.ComputeHash(directory), entry.ContentHash, StringComparison.OrdinalIgnoreCase)) continue;
                manifest = PluginDiscovery.ReadFolder(directory, source).Manifest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (manifest is not null && manifest.Id == entry.Id && manifest.Version == entry.Version)
                yield return manifest;
        }
    }

    private static PluginInventory WithReplacedThemes(PluginInventory current, BundledPluginCatalog bundled,
        IReadOnlyDictionary<string, string> replacedThemeIds)
    {
        if (current.ReadError is not null || bundled.ReadError is not null) return current;

        var entries = current.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var (previousId, packageId) in replacedThemeIds)
        {
            if (!entries.TryGetValue(previousId, out var previous) || previous.Source != BundledSource
                || bundled.Find(packageId) is not { } package) continue;

            entries.Remove(previousId);
            if (!previous.Enabled) entries.TryAdd(packageId, DisabledPackage(package, ["theme"]));
        }
        return entries.Count == current.Entries.Count && current.Entries.All(entry => entries.ContainsKey(entry.Id))
            ? current : new PluginInventory(entries.Values);
    }

    private static PluginInventoryEntry DisabledPackage(BundledPluginEntry package, IReadOnlyList<string> roles) =>
        new(package.Id, package.Version, roles, BundledSource, null, package.ContentHash, "none", Enabled: false, Approved: false);

    internal void Update(Func<PluginInventory, IEnumerable<PluginInventoryEntry>> change, Action? rollback = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = Read();
        if (current.ReadError is not null) throw new InvalidOperationException("The plugin inventory could not be read.");

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = new PluginInventory(change(current)).ToJson();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
                writer.Write(json);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { rollback?.Invoke(); }
            finally { File.Delete(temporary); }
            throw;
        }
    }
}
