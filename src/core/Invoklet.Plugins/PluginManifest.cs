using System.Collections.ObjectModel;
using System.Text.Json;

namespace Invoklet.Plugins;

/// <summary>The assembly and type the host loads for a plugin with code.</summary>
/// <param name="Assembly">The file name of the assembly in the plugin's folder, such as <c>Invoklet.Connectors.Postgres.dll</c>.</param>
/// <param name="Type">The full name of the type implementing <see cref="IInvokletPlugin"/>.</param>
public sealed record PluginEntryPoint(string Assembly, string Type);

/// <summary>
/// The <c>invoklet-plugin.json</c> beside a plugin: what the host knows before it loads anything. Read by
/// <see cref="PluginManifestReader"/>, which validates it; a manifest in hand is a valid one.
/// </summary>
/// <param name="Id">The stable id: lower-case segments joined by dots, such as <c>invoklet.connector.postgres</c>.</param>
/// <param name="Version">The plugin's version, <c>major.minor.patch</c> with an optional pre-release tag.</param>
/// <param name="DisplayName">What the Plugins page shows.</param>
/// <param name="Publisher">Who made it, as shown to the user; null when the manifest doesn't say.</param>
/// <param name="Roles">Each role the plugin takes, with the contract version it was written against.</param>
/// <param name="Entry">The code to load; null for a plugin of data roles only.</param>
/// <param name="DependsOn">Ids of plugins that must load first and whose assemblies this one shares.</param>
/// <param name="MinAppVersion">The oldest Invoklet the plugin works with; null when any.</param>
/// <param name="Extra">Fields this Invoklet doesn't know, kept for the roles that read them (a theme's <c>assets</c>) and for newer manifests.</param>
public sealed record PluginManifest(
    string Id,
    string Version,
    string DisplayName,
    string? Publisher,
    IReadOnlyDictionary<PluginRole, int> Roles,
    PluginEntryPoint? Entry,
    IReadOnlyList<string> DependsOn,
    Version? MinAppVersion,
    IReadOnlyDictionary<string, JsonElement> Extra)
{
    public IReadOnlyDictionary<PluginRole, int> Roles { get; } =
        new ReadOnlyDictionary<PluginRole, int>(new Dictionary<PluginRole, int>(Roles));

    public IReadOnlyList<string> DependsOn { get; } = Array.AsReadOnly(DependsOn.ToArray());

    public IReadOnlyDictionary<string, JsonElement> Extra { get; } =
        new ReadOnlyDictionary<string, JsonElement>(Extra.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal));

    public bool HasRole(PluginRole role) => Roles.ContainsKey(role);

    /// <summary>Whether any of the roles is implemented by code, which is when <see cref="Entry"/> is required.</summary>
    public bool RequiresCode => Roles.Keys.Any(PluginRoles.RequiresCode);
}
