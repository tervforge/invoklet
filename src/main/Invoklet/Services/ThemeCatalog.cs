using System.Text.Json;
using Invoklet.Avalonia.Themes;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using TextMateSharp.Grammars;

namespace Invoklet.Services;

public interface IThemeCatalog
{
    IReadOnlyList<InterfaceTheme> Interfaces { get; }
    IReadOnlyList<EditorTheme> Editors { get; }
}

/// <summary>Collects verified theme assets during startup. Consumers take a snapshot after plugin loading.</summary>
internal sealed class ThemeCatalog : IThemeCatalog, IPluginRoleHandler
{
    private readonly Dictionary<string, ThemeDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
    private readonly List<InterfaceTheme> _interfaces = [];
    private readonly List<EditorTheme> _editors = [];
    private readonly ThemeDefinitionReader _reader = new();
    private readonly EditorThemeReader _editorReader = new();
    private readonly ThemeResolver _resolver = new(BuiltInInterfaceThemes.Defaults);

    public IReadOnlyList<InterfaceTheme> Interfaces => _interfaces.AsReadOnly();
    public IReadOnlyList<EditorTheme> Editors => _editors.AsReadOnly();
    public PluginRole Role => PluginRole.Theme;
    public int ContractVersion => PluginContracts.Theme;

    public PluginRoleRegistration Prepare(PluginRegistration registration)
    {
        var context = registration.Context;
        var id = context.Manifest.Id;
        var source = context.Trust == PluginTrust.Bundled ? null : context.Manifest.Publisher ?? "Plugin";
        var warnings = new List<PluginDiagnostic>();
        var definitions = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);
        foreach (var asset in ThemePaths(context.Manifest))
        {
            using var stream = OpenThemeAsset(registration, asset);
            using var text = new StreamReader(stream);
            var result = _reader.Read(text.ReadToEnd());
            if (result.Definition is not { } definition)
                throw new PluginRegistrationException(string.Join(" ", result.Diagnostics.Select(DescribeDiagnostic)));
            if (!PluginManifestReader.IsValidId(definition.Id) || _definitions.ContainsKey(definition.Id)
                || BuiltInInterfaceThemes.All.Any(theme => theme.Id == definition.Id)
                || !definitions.TryAdd(definition.Id, definition))
                throw new PluginRegistrationException("The theme id is invalid or already registered.");

            foreach (var diagnostic in result.Diagnostics)
                warnings.Add(Warning(id, DescribeDiagnostic(diagnostic),
                    $"{definition.Id}: {diagnostic.Field}: {diagnostic.Message}"));
        }

        var available = BuiltInInterfaceThemes.Definitions.AddRange(_definitions).AddRange(definitions);
        foreach (var definition in definitions.Values)
            if (definition.Extends is { } parent && _owners.TryGetValue(parent, out var owner)
                && !context.Manifest.DependsOn.Contains(owner, StringComparer.Ordinal))
                throw new PluginRegistrationException("The parent theme package must be declared in dependsOn.");

        var interfaces = new List<InterfaceTheme>();
        var editors = new List<EditorTheme>();
        foreach (var definition in definitions.Values)
        {
            var result = _resolver.Resolve(definition, available);
            if (result.Theme is not { } resolved)
                throw new PluginRegistrationException(result.Diagnostics[0].Code == ThemeDiagnosticCode.InheritanceCycle
                    ? "Theme inheritance contains a cycle." : "The parent theme could not be found.");
            var theme = ThemeBuilder.Describe(resolved) with { Source = source };
            interfaces.Add(theme);

            var fallback = theme.IsDark ? new EditorTheme("Dark+", ThemeName.DarkPlus, true) : new EditorTheme("Light+", ThemeName.Light, false);
            foreach (var (editorId, asset) in resolved.EditorThemes)
            {
                if (asset.ThemeId != definition.Id) continue;
                try
                {
                    using var stream = registration.OpenAsset(asset.Path);
                    var editor = _editorReader.Read(stream, editorId, fallback);
                    if (editor.Diagnostic is { } diagnostic) warnings.Add(Warning(id, "An editor theme could not be loaded.", diagnostic));
                    else editors.Add(editor.Theme with { Id = EditorId(definition.Id, editorId), Source = source });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    warnings.Add(Warning(id, "An editor theme asset could not be read.", ex.Message));
                }
            }
        }

        return new PluginRoleRegistration(
            () =>
            {
                foreach (var (key, definition) in definitions)
                {
                    _definitions.Add(key, definition);
                    _owners.Add(key, id);
                }
                _interfaces.AddRange(interfaces);
                _editors.AddRange(editors);
            },
            () =>
            {
                foreach (var key in definitions.Keys)
                {
                    _definitions.Remove(key);
                    _owners.Remove(key);
                }
                foreach (var theme in interfaces) _interfaces.Remove(theme);
                foreach (var theme in editors) _editors.Remove(theme);
            })
        { Warnings = warnings.AsReadOnly() };
    }

    public static string EditorId(string themeId, string editorId) => $"{themeId}/{editorId}";

    private static PluginDiagnostic Warning(string id, string message, string detail) =>
        PluginDiagnostic.Warning(id, PluginDiagnosticCode.ThemeAssetInvalid, message, detail);

    private static IEnumerable<string> ThemePaths(PluginManifest manifest)
    {
        if (!manifest.Extra.TryGetValue("assets", out var assets) || assets.ValueKind != JsonValueKind.Object
            || !assets.TryGetProperty("themes", out var themes) || themes.ValueKind != JsonValueKind.Array || themes.GetArrayLength() == 0)
            throw new PluginRegistrationException("A theme plugin must list its assets in assets.themes.");

        foreach (var theme in themes.EnumerateArray())
        {
            if (theme.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(theme.GetString()))
                throw new PluginRegistrationException("Each theme asset must be a relative path.");
            yield return theme.GetString()!;
        }
    }

    private static string DescribeDiagnostic(ThemeDiagnostic diagnostic) =>
        diagnostic.Field.Length is > 0 and <= 128 && diagnostic.Field.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? $"{diagnostic.Field}: {diagnostic.Message}"
            : diagnostic.Message;

    private static Stream OpenThemeAsset(PluginRegistration registration, string asset)
    {
        try
        {
            return registration.OpenAsset(asset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new PluginRegistrationException("A theme asset listed in assets.themes could not be read or verified.", ex);
        }
    }
}
