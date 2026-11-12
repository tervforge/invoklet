namespace Invoklet.Plugins.Hosting;

/// <summary>A folder whose sub-folders hold plugins, and what loading from it requires.</summary>
/// <param name="Directory">The root to scan; a missing one is simply empty.</param>
/// <param name="Trust">What every plugin found under it is held to.</param>
public sealed record PluginSource(string Directory, PluginTrust Trust);

/// <summary>A manifest found by <see cref="PluginDiscovery"/>: read when it could be, with what was wrong with it otherwise.</summary>
/// <param name="Id">The manifest's id, or the folder's name for a manifest without a usable one.</param>
/// <param name="Manifest">Null for a manifest that failed to read; <paramref name="Diagnostics"/> says why.</param>
/// <param name="Directory">The folder holding the manifest: the plugin's folder.</param>
public sealed record DiscoveredPlugin(
    string Id,
    PluginManifest? Manifest,
    string Directory,
    PluginSource Source,
    IReadOnlyList<PluginDiagnostic> Diagnostics);
