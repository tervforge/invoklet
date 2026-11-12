namespace Invoklet.Plugins;

/// <summary>
/// The contract versions this Invoklet supports, one per role. A manifest names the version its code was written against; a version
/// this list doesn't have is rejected before any of the plugin's code runs. Additions to a role's contract keep its version (default
/// interface members); a change a plugin built against the old contract can't survive makes a new version.
/// </summary>
public static class PluginContracts
{
    public const int Theme = 1;
    public const int Connection = 1;
    public const int Language = 1;
    public const int Platform = 1;
    public const int Library = 1;

    /// <summary>The name of the manifest file beside a plugin's assembly or theme files.</summary>
    public const string ManifestFileName = "invoklet-plugin.json";

    /// <summary>The versions of a role this Invoklet can host.</summary>
    public static IReadOnlyList<int> SupportedVersions(PluginRole role) => role switch
    {
        PluginRole.Theme => [Theme],
        PluginRole.Connection => [Connection],
        PluginRole.Language => [Language],
        PluginRole.Platform => [Platform],
        PluginRole.Library => [Library],
        _ => [],
    };
}
