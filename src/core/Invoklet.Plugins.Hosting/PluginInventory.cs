using System.Text.Json;
using System.Text.Json.Serialization;

namespace Invoklet.Plugins.Hosting;

public sealed record PluginInventoryEntry(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Version,
    [property: JsonRequired] IReadOnlyList<string> Roles,
    [property: JsonRequired] string Source,
    string? Publisher,
    [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string SignatureStatus,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] bool Approved);

/// <summary>The desired user plugin configuration. One selected version per id.</summary>
public sealed class PluginInventory
{
    public const string FileName = "plugins.json";
    public static PluginInventory Empty { get; } = new([]);

    private readonly IReadOnlyDictionary<string, PluginInventoryEntry> _entries;

    public PluginInventory(IEnumerable<PluginInventoryEntry> entries)
    {
        var indexed = new Dictionary<string, PluginInventoryEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || !PluginCatalogJson.IsValid(entry.Id, entry.Version, entry.ContentHash)
                || entry.Roles is not { Count: > 0 } || entry.Roles.Any(role => role is null || PluginRoles.Parse(role) is null)
                || entry.Roles.Distinct(StringComparer.Ordinal).Count() != entry.Roles.Count
                || string.IsNullOrWhiteSpace(entry.Source) || entry.SignatureStatus != "none")
                throw new JsonException("Invalid plugin inventory entry.");
            var snapshot = entry with
            {
                Roles = Array.AsReadOnly(entry.Roles.Order(StringComparer.Ordinal).ToArray()),
                ContentHash = entry.ContentHash.ToLowerInvariant(),
            };
            if (!indexed.TryAdd(entry.Id, snapshot)) throw new JsonException("Duplicate plugin id in the inventory.");
        }
        _entries = indexed;
        Entries = Array.AsReadOnly(indexed.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
    }

    private PluginInventory(string readError) : this(Array.Empty<PluginInventoryEntry>()) => ReadError = readError;

    public IReadOnlyList<PluginInventoryEntry> Entries { get; }
    internal string? ReadError { get; }
    internal PluginInventoryEntry? Find(string id) => _entries.GetValueOrDefault(id);

    public static PluginInventory Read(string path) => PluginCatalogJson.Read<PluginInventoryEntry, PluginInventory>(path,
        entries => new PluginInventory(entries), Empty, error => new PluginInventory(error));

    public string ToJson() => JsonSerializer.Serialize(Entries, PluginCatalogJson.Options);

    internal bool SameEntry(string id, PluginInventory other)
    {
        var before = Find(id);
        var after = other.Find(id);
        if (before is null || after is null) return before == after;
        return before == (after with { Roles = before.Roles }) && before.Roles.SequenceEqual(after.Roles);
    }
}

public sealed record BundledPluginEntry(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Version,
    [property: JsonRequired] string ContentHash);

/// <summary>The catalogue shipped alongside the application.</summary>
public sealed class BundledPluginCatalog
{
    public const string FileName = "bundled.json";
    public static BundledPluginCatalog Empty { get; } = new([]);

    private readonly Dictionary<string, BundledPluginEntry> _entries = new(StringComparer.Ordinal);

    public BundledPluginCatalog(IEnumerable<BundledPluginEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null || !PluginCatalogJson.IsValid(entry.Id, entry.Version, entry.ContentHash))
                throw new JsonException("Invalid bundled plugin entry.");
            if (!_entries.TryAdd(entry.Id, entry)) throw new JsonException("Duplicate plugin id in the bundled catalogue.");
        }
    }

    private BundledPluginCatalog(string readError) : this(Array.Empty<BundledPluginEntry>()) => ReadError = readError;

    internal string? ReadError { get; }
    internal BundledPluginEntry? Find(string id) => _entries.GetValueOrDefault(id);

    public static BundledPluginCatalog Read(string path) => PluginCatalogJson.Read<BundledPluginEntry, BundledPluginCatalog>(path,
        entries => new BundledPluginCatalog(entries), Empty, error => new BundledPluginCatalog(error));

    public string ToJson() => JsonSerializer.Serialize(_entries.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal), PluginCatalogJson.Options);
}

internal static class PluginCatalogJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
        WriteIndented = true,
    };

    internal static bool IsValid(string? id, string? version, string? hash) =>
        id is not null && PluginManifestReader.IsValidId(id) && version is not null && PluginManifestReader.IsValidVersion(version)
        && hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    internal static TCatalog Read<TEntry, TCatalog>(string path, Func<TEntry[], TCatalog> create, TCatalog empty, Func<string, TCatalog> invalid)
    {
        try
        {
            using var file = File.OpenRead(path);
            return create(JsonSerializer.Deserialize<TEntry[]>(file, Options) ?? throw new JsonException("Missing plugin entries."));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return empty; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return invalid(ex.Message); }
    }
}
