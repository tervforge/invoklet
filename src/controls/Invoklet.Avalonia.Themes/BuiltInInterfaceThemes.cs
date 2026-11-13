using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Invoklet.Avalonia.Themes;

/// <summary>The bundled interface themes in the order used by Appearance.</summary>
public static class BuiltInInterfaceThemes
{
    private const string IdPrefix = "invoklet.theme.";

    public static ImmutableDictionary<string, ThemeDefinition> Definitions { get; } = LoadDefinitions();

    public static ThemeDefaults Defaults { get; } = new(Definitions[IdPrefix + "midnight"].Resources,
        Definitions[IdPrefix + "warm-light"].Resources, ThemeBuilder.DefaultMetrics);

    private static readonly ImmutableArray<(InterfaceTheme Interface, ResolvedTheme Theme)> Entries = Load();

    public static IReadOnlyList<InterfaceTheme> All { get; } = Entries.Select(entry => entry.Interface).ToImmutableArray();

    public static ThemeVariant GetVariant(string id) => All.First(theme => Equals(theme.Variant.Key, id)).Variant;

    internal static void RegisterPalettes(IResourceDictionary resources)
    {
        foreach (var (theme, resolved) in Entries)
            resources.ThemeDictionaries.Add(theme.Variant, ThemeBuilder.BuildPalette(resolved));
    }

    private static ImmutableArray<(InterfaceTheme, ResolvedTheme)> Load()
    {
        var resolver = new ThemeResolver(Defaults);

        return Assets.Select(asset => Definitions[IdPrefix + asset]).Select(definition =>
        {
            var result = resolver.Resolve(definition, Definitions);
            var resolved = result.Theme ?? throw new InvalidOperationException(DescribeFailure(definition.Id, result.Diagnostics));
            return (ThemeBuilder.Describe(resolved), resolved);
        }).ToImmutableArray();
    }

    private static string[] Assets => ["midnight", "warm-light"];

    private static ImmutableDictionary<string, ThemeDefinition> LoadDefinitions()
    {
        var reader = new ThemeDefinitionReader();
        return Assets.Select(asset => ReadTheme(reader, asset)).ToImmutableDictionary(theme => theme.Id, StringComparer.Ordinal);
    }

    private static ThemeDefinition ReadTheme(ThemeDefinitionReader reader, string asset)
    {
        var theme = reader.ReadBuiltIn($"Themes/{asset}.json");
        if (theme.Id != IdPrefix + asset || theme.Base is null)
            throw new InvalidOperationException($"The built-in theme '{asset}' has an unexpected id or base.");
        return theme;
    }

    private static string DescribeFailure(string theme, IReadOnlyList<ThemeDiagnostic> diagnostics) =>
        $"The built-in theme '{theme}' is invalid. {string.Join(" ", diagnostics.Select(item => $"{item.Field}: {item.Message}"))}";
}
