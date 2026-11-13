namespace Invoklet.Plugins;

public enum PluginDiagnosticSeverity
{
    /// <summary>Something to know; the plugin still loads.</summary>
    Warning,

    /// <summary>The plugin is rejected.</summary>
    Error,
}

/// <summary>Why a plugin was rejected or what to know about one that loaded. Codes are stable: the Plugins page and the log key on them.</summary>
public enum PluginDiagnosticCode
{
    ManifestUnreadable,
    ManifestInvalid,
    DuplicateId,
    UnsupportedRole,
    UnsupportedContractVersion,
    ApplicationTooOld,
    DependencyMissing,
    DependencyRejected,
    DependencyCycle,
    EntryAssemblyMissing,
    EntryAssemblyUnloadable,
    EntryTypeMissing,
    EntryTypeInvalid,
    DescriptorMismatch,
    RegistrationFailed,
    DirectoryUnreadable,
    PluginSourceNotConfigured,
    InventoryUnreadable,
    PluginNotListed,
    PluginDisabled,
    PluginNotApproved,
    PluginContentChanged,
    PluginContentUnreadable,
    DevelopmentPlugin,
    ThemeAssetInvalid,
}

/// <summary>
/// One finding about a plugin. <see cref="Message"/> is for the user: it names the plugin and the reason, never a secret or a path in
/// the user's profile. <see cref="Detail"/> is for the log and may carry the exception or the path.
/// </summary>
public sealed record PluginDiagnostic(
    string PluginId,
    PluginDiagnosticSeverity Severity,
    PluginDiagnosticCode Code,
    string Message,
    string? Detail = null)
{
    public static PluginDiagnostic Error(string pluginId, PluginDiagnosticCode code, string message, string? detail = null) =>
        new(pluginId, PluginDiagnosticSeverity.Error, code, message, detail);

    public static PluginDiagnostic Warning(string pluginId, PluginDiagnosticCode code, string message, string? detail = null) =>
        new(pluginId, PluginDiagnosticSeverity.Warning, code, message, detail);

    public bool IsError => Severity == PluginDiagnosticSeverity.Error;

    public override string ToString() => $"{Severity} {Code} [{PluginId}]: {Message}";
}
