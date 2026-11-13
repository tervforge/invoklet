using System.Collections.Immutable;

namespace Invoklet.Avalonia.Themes;

/// <summary>Host-supplied Midnight and Warm Light palettes and the shared metric and icon defaults.</summary>
public sealed record ThemeDefaults(ImmutableDictionary<string, object> Dark, ImmutableDictionary<string, object> Light,
    ImmutableDictionary<string, object> Shared);

/// <summary>An inherited editor asset keeps the id of the theme whose package contains it.</summary>
public sealed record ThemeEditorAsset(string ThemeId, string Path);

public sealed record ResolvedTheme(string Id, string Name, ThemeBase Base, ImmutableDictionary<string, object> Resources,
    string? EditorDefault, ImmutableDictionary<string, ThemeEditorAsset> EditorThemes);

public sealed record ThemeResolutionResult(ResolvedTheme? Theme, IReadOnlyList<ThemeDiagnostic> Diagnostics);

/// <summary>Overlays validated theme definitions on the host's built-in resources.</summary>
public sealed class ThemeResolver(ThemeDefaults defaults, IReadOnlyDictionary<string, ThemeKey>? catalog = null)
{
    private readonly IReadOnlyDictionary<string, ThemeKey> _catalog = catalog ?? ThemeKeyCatalog.All;

    public ThemeResolutionResult Resolve(ThemeDefinition theme, IReadOnlyDictionary<string, ThemeDefinition> themes)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(themes);

        var chain = new List<ThemeDefinition> { theme };
        var visited = new HashSet<string>(StringComparer.Ordinal) { theme.Id };
        while (chain[^1].Extends is { } parentId)
        {
            if (!visited.Add(parentId))
            {
                var cycle = string.Join(" → ", chain.Select(item => item.Id).Append(parentId));
                return Failure(theme.Id, ThemeDiagnosticCode.InheritanceCycle, $"Theme inheritance contains a cycle: {cycle}.");
            }

            if (!themes.TryGetValue(parentId, out var parent))
                return Failure(theme.Id, ThemeDiagnosticCode.MissingParent, $"Theme '{chain[^1].Id}' extends unknown theme '{parentId}'.");

            chain.Add(parent);
        }

        var themeBase = chain.Select(item => item.Base).FirstOrDefault(value => value.HasValue) ?? ThemeBase.Dark;
        var palette = themeBase == ThemeBase.Dark ? defaults.Dark : defaults.Light;
        var resources = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in defaults.Shared) resources[name] = value;
        foreach (var (name, value) in palette) resources[name] = value;

        string? editorDefault = null;
        var editorThemes = ImmutableDictionary.CreateBuilder<string, ThemeEditorAsset>(StringComparer.Ordinal);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var current = chain[i];
            foreach (var (name, value) in current.Resources)
            {
                if (_catalog.TryGetValue(name, out var key) && key.AliasFor is { } canonical)
                {
                    // A canonical entry wins when both spellings occur in the same theme.
                    if (!current.Resources.ContainsKey(canonical)) resources[canonical] = value;
                }
                else
                {
                    resources[name] = value;
                }
            }

            if (current.Editor is { } editor)
            {
                editorDefault = editor.Default ?? editorDefault;
                foreach (var (id, path) in editor.Themes)
                    editorThemes[id] = new ThemeEditorAsset(current.Id, path);
            }
        }

        foreach (var key in _catalog.Values)
            if (key.AliasFor is { } canonical && resources.TryGetValue(canonical, out var value))
                resources[key.Name] = value;

        return new ThemeResolutionResult(new ResolvedTheme(theme.Id, theme.Name, themeBase, resources.ToImmutable(),
            editorDefault, editorThemes.ToImmutable()), []);
    }

    private static ThemeResolutionResult Failure(string id, ThemeDiagnosticCode code, string message) =>
        new(null, [new ThemeDiagnostic(code, id, "extends", message)]);
}
