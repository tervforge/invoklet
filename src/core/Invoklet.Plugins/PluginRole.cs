namespace Invoklet.Plugins;

/// <summary>What a plugin adds to Invoklet. A package may take several roles; each is checked and registered on its own.</summary>
public enum PluginRole
{
    /// <summary>Colours, metrics, icons and editor themes: data files, no code.</summary>
    Theme,

    /// <summary>A kind of connection a script's <c>connections:</c> can name.</summary>
    Connection,

    /// <summary>A source language: editor services and the code a script is written in.</summary>
    Language,

    /// <summary>What compiles or interprets, runs and debugs a language, and manages its packages.</summary>
    Platform,

    /// <summary>Code other plugins depend on; registers nothing of its own.</summary>
    Library,
}

public static class PluginRoles
{
    /// <summary>The roles in the order their registrations run: a platform before the languages on it, a language before the
    /// connections it represents, themes last.</summary>
    public static readonly IReadOnlyList<PluginRole> RegistrationOrder =
        Array.AsReadOnly<PluginRole>([PluginRole.Library, PluginRole.Platform, PluginRole.Language, PluginRole.Connection, PluginRole.Theme]);

    /// <summary>The manifest's name of a role.</summary>
    public static string Name(PluginRole role) => role switch
    {
        PluginRole.Theme => "theme",
        PluginRole.Connection => "connection",
        PluginRole.Language => "language",
        PluginRole.Platform => "platform",
        PluginRole.Library => "library",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>The role a manifest names, or null for a name this Invoklet doesn't know.</summary>
    public static PluginRole? Parse(string name) => name switch
    {
        "theme" => PluginRole.Theme,
        "connection" => PluginRole.Connection,
        "language" => PluginRole.Language,
        "platform" => PluginRole.Platform,
        "library" => PluginRole.Library,
        _ => null,
    };

    /// <summary>Whether the role is implemented by code the host loads, as against data files it reads.</summary>
    public static bool RequiresCode(PluginRole role) => role != PluginRole.Theme;

    /// <summary>Whether the host registers the role through a handler; a library only exists for its dependants.</summary>
    public static bool IsRegistered(PluginRole role) => role != PluginRole.Library;
}
