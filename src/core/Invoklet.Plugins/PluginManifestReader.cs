using System.Text.Json;
using System.Text.RegularExpressions;

namespace Invoklet.Plugins;

/// <summary>A manifest read: the manifest when it is valid, and what was wrong with it otherwise (or worth knowing anyway).</summary>
public sealed record PluginManifestResult(PluginManifest? Manifest, IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public bool IsValid => Manifest is not null;
}

/// <summary>
/// Reads and validates <c>invoklet-plugin.json</c>. Every problem is a diagnostic, never an exception: a malformed manifest is a
/// plugin the host rejects and reports, not a failed start. Fields the reader doesn't know are kept, not refused, so a manifest for
/// a newer Invoklet still describes its plugin here.
/// </summary>
public static partial class PluginManifestReader
{
    [GeneratedRegex(@"\A[a-z0-9]+(\.[a-z0-9]+(-[a-z0-9]+)*)+\z")]
    private static partial Regex IdPattern();

    private const string NumericIdentifier = "(?:0|[1-9][0-9]*)";
    private const string PrereleaseIdentifier = "(?:" + NumericIdentifier + "|[0-9]*[A-Za-z-][0-9A-Za-z-]*)";
    private const string BuildIdentifier = "[0-9A-Za-z-]+";

    [GeneratedRegex(@"\A" + NumericIdentifier + @"\." + NumericIdentifier + @"\." + NumericIdentifier
        + "(?:-" + PrereleaseIdentifier + @"(?:\." + PrereleaseIdentifier + ")*)?"
        + @"(?:\+" + BuildIdentifier + @"(?:\." + BuildIdentifier + @")*)?\z")]
    private static partial Regex VersionPattern();

    /// <summary>Whether a text is a well-formed plugin id.</summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    /// <summary>Whether a text is a well-formed plugin version.</summary>
    public static bool IsValidVersion(string version) => VersionPattern().IsMatch(version);

    /// <param name="json">The manifest's text.</param>
    /// <param name="fallbackId">What to name the plugin in diagnostics when the manifest has no usable id: its folder's name.</param>
    public static PluginManifestResult Read(string json, string? fallbackId = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var pluginId = string.IsNullOrWhiteSpace(fallbackId) ? "?" : fallbackId;
        try
        {
            return ReadManifest(json, pluginId);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new PluginManifestResult(null, [PluginDiagnostic.Error(pluginId, PluginDiagnosticCode.ManifestUnreadable,
                $"The manifest of '{pluginId}' is not valid JSON.", ex.Message)]);
        }
    }

    private static PluginManifestResult ReadManifest(string json, string pluginId)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            AllowDuplicateProperties = false,
        });
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(PluginDiagnostic.Error(pluginId, PluginDiagnosticCode.ManifestInvalid,
                    $"The manifest of '{pluginId}' must be a JSON object."));
                return new PluginManifestResult(null, diagnostics);
            }

            var id = ReadString(root, "id");
            if (id is not null && IsValidId(id)) pluginId = id;
            else diagnostics.Add(Invalid(pluginId, "'id' must be lower-case segments joined by dots, such as 'invoklet.connector.postgres'."));

            var version = ReadString(root, "version");
            if (version is null || !IsValidVersion(version))
                diagnostics.Add(Invalid(pluginId, "'version' must be 'major.minor.patch', optionally with a pre-release tag."));

            var displayName = ReadString(root, "displayName");
            if (string.IsNullOrWhiteSpace(displayName))
                diagnostics.Add(Invalid(pluginId, "'displayName' is required."));

            var publisher = ReadString(root, "publisher");
            var roles = ReadRoles(root, pluginId, diagnostics);
            var entry = ReadEntry(root, pluginId, diagnostics);
            var requiresCode = roles.Keys.Any(PluginRoles.RequiresCode);
            if (entry is null && requiresCode)
                diagnostics.Add(Invalid(pluginId, "'entry' with 'assembly' and 'type' is required for a plugin with code (every role but a theme)."));
            if (entry is not null && !requiresCode)
                diagnostics.Add(Invalid(pluginId, "'entry' is not allowed for a data-only plugin."));
            var dependsOn = ReadDependsOn(root, pluginId, id, diagnostics);
            var minAppVersion = ReadMinAppVersion(root, pluginId, diagnostics);
            var extra = ReadExtra(root);

            if (diagnostics.Any(d => d.IsError)) return new PluginManifestResult(null, diagnostics);

            var manifest = new PluginManifest(id!, version!, displayName!, publisher, roles, entry, dependsOn, minAppVersion, extra);
            return new PluginManifestResult(manifest, diagnostics);
        }
    }

    private static readonly string[] KnownFields = ["id", "version", "displayName", "publisher", "roles", "entry", "dependsOn", "minAppVersion"];

    private static PluginDiagnostic Invalid(string pluginId, string what) =>
        PluginDiagnostic.Error(pluginId, PluginDiagnosticCode.ManifestInvalid, $"The manifest of '{pluginId}' is invalid: {what}");

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Dictionary<PluginRole, int> ReadRoles(JsonElement root, string pluginId, List<PluginDiagnostic> diagnostics)
    {
        var roles = new Dictionary<PluginRole, int>();
        if (!root.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Invalid(pluginId, "'roles' must be an object of role names to contract versions, such as {\"connection\": 1}."));
            return roles;
        }

        foreach (var property in rolesElement.EnumerateObject())
        {
            var role = PluginRoles.Parse(property.Name);
            if (role is null)
            {
                diagnostics.Add(PluginDiagnostic.Error(pluginId, PluginDiagnosticCode.UnsupportedRole,
                    $"'{pluginId}' declares a role this Invoklet doesn't know.", $"Unknown role: {property.Name}"));
                continue;
            }

            int? contractVersion = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(property.Value.GetString(), out var n) => n,
                _ => null,
            };
            if (contractVersion is not > 0)
            {
                diagnostics.Add(Invalid(pluginId, $"the contract version of role '{property.Name}' must be a positive whole number."));
                continue;
            }
            roles[role.Value] = contractVersion.Value;
        }

        if (roles.Count == 0 && !diagnostics.Any(d => d.IsError))
            diagnostics.Add(Invalid(pluginId, "'roles' must name at least one role."));
        return roles;
    }

    private static PluginEntryPoint? ReadEntry(JsonElement root, string pluginId, List<PluginDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("entry", out var entry)) return null;
        if (entry.ValueKind == JsonValueKind.Null) return null;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Invalid(pluginId, "'entry' must be an object with 'assembly' and 'type'."));
            return null;
        }

        var assembly = ReadString(entry, "assembly");
        var type = ReadString(entry, "type");
        var valid = true;
        if (string.IsNullOrWhiteSpace(assembly) || assembly.IndexOfAny(['/', '\\', ':']) >= 0 || assembly.Any(char.IsControl)
            || !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || assembly.Contains(".."))
        {
            diagnostics.Add(Invalid(pluginId, "'entry.assembly' must be the file name of a .dll in the plugin's folder, without a path."));
            valid = false;
        }
        if (string.IsNullOrWhiteSpace(type))
        {
            diagnostics.Add(Invalid(pluginId, "'entry.type' must be the full name of the plugin's entry type."));
            valid = false;
        }
        return valid ? new PluginEntryPoint(assembly!, type!) : null;
    }

    private static List<string> ReadDependsOn(JsonElement root, string pluginId, string? ownId, List<PluginDiagnostic> diagnostics)
    {
        var dependsOn = new List<string>();
        if (!root.TryGetProperty("dependsOn", out var element) || element.ValueKind == JsonValueKind.Null) return dependsOn;
        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Invalid(pluginId, "'dependsOn' must be an array of plugin ids."));
            return dependsOn;
        }

        foreach (var item in element.EnumerateArray())
        {
            var dependency = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (dependency is null || !IsValidId(dependency))
            {
                diagnostics.Add(Invalid(pluginId, "every entry of 'dependsOn' must be a plugin id."));
                continue;
            }
            if (string.Equals(dependency, ownId, StringComparison.Ordinal))
            {
                diagnostics.Add(Invalid(pluginId, "a plugin can't depend on itself."));
                continue;
            }
            if (!dependsOn.Contains(dependency, StringComparer.Ordinal)) dependsOn.Add(dependency);
        }
        return dependsOn;
    }

    private static Version? ReadMinAppVersion(JsonElement root, string pluginId, List<PluginDiagnostic> diagnostics)
    {
        var text = ReadString(root, "minAppVersion");
        if (text is null)
        {
            if (root.TryGetProperty("minAppVersion", out var present) && present.ValueKind != JsonValueKind.Null)
                diagnostics.Add(Invalid(pluginId, "'minAppVersion' must be a version text such as '0.13.0'."));
            return null;
        }
        if (System.Version.TryParse(text, out var version)) return version;
        diagnostics.Add(Invalid(pluginId, "'minAppVersion' must be a version text such as '0.13.0'."));
        return null;
    }

    private static Dictionary<string, JsonElement> ReadExtra(JsonElement root)
    {
        var extra = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
                extra[property.Name] = property.Value;
        return extra;
    }
}
