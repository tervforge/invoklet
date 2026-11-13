namespace Invoklet.Plugins.Hosting;

/// <summary>Updates the desired startup state without changing the current plugin session.</summary>
public sealed class PluginInventoryStore(string path)
{
    public const string BundledSource = "bundled";

    public PluginInventory Read() => PluginInventory.Read(path);

    public PluginInventory Read(BundledPluginCatalog bundled, IReadOnlyDictionary<string, string> replacedThemeIds) =>
        WithReplacedThemes(Read(), bundled, replacedThemeIds);

    public void SetEnabled(string id, bool enabled, BundledPluginCatalog bundled,
        IReadOnlyDictionary<string, string>? replacedThemeIds = null, IReadOnlyList<string>? bundledRoles = null)
    {
        if (bundled.ReadError is not null) throw new InvalidOperationException("The bundled plugin catalogue could not be read.");
        Update(current =>
        {
            if (replacedThemeIds is not null)
                current = WithReplacedThemes(current, bundled, replacedThemeIds);
            var entry = current.Find(id);
            var entries = current.Entries.Where(item => item.Id != id);
            if (bundled.Find(id) is { } package)
            {
                // The shipped catalogue remains the authority for version, contents and roles.
                return enabled ? entries : entries.Append(DisabledPackage(package, bundledRoles ?? ["theme"]));
            }

            if (entry is not { Approved: true } || entry.Source == BundledSource)
                throw new InvalidOperationException("The plugin has no approved inventory entry.");
            return entries.Append(entry with { Enabled = enabled });
        });
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
