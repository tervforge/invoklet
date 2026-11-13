using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Invoklet.Avalonia.Themes;

public static class ThemeBuilder
{
    private static readonly Lazy<ImmutableDictionary<string, object>> BuiltInMetrics =
        new(() => new ThemeDefinitionReader().ReadBuiltIn("Metrics.json").Resources);

    internal static ImmutableDictionary<string, object> DefaultMetrics => BuiltInMetrics.Value;

    public static InterfaceTheme Describe(ResolvedTheme theme)
    {
        var isDark = theme.Base == ThemeBase.Dark;
        return new InterfaceTheme(theme.Name, new ThemeVariant(theme.Id, isDark ? ThemeVariant.Dark : ThemeVariant.Light), isDark)
        {
            Definition = theme,
        };
    }

    public static ResourceDictionary BuildPalette(ResolvedTheme theme)
    {
        var dictionary = new ResourceDictionary();
        foreach (var key in ThemeKeyCatalog.Palette)
        {
            var value = theme.Resources[key.Name];
            dictionary.Add(key.Name, value is Color color && !key.IsRawColor ? new ImmutableSolidColorBrush(color) : value);
        }

        return dictionary;
    }

    public static ResourceDictionary BuildMetrics(ResolvedTheme? theme = null)
    {
        var dictionary = new ResourceDictionary();
        foreach (var key in ThemeKeyCatalog.Metrics)
        {
            var name = key.AliasFor ?? key.Name;
            var value = theme?.Resources.TryGetValue(name, out var resolved) == true ? resolved : DefaultMetrics[name];
            dictionary.Add(key.Name, value);
        }

        return dictionary;
    }

    public static ResourceDictionary BuildActiveResources(ResolvedTheme? theme)
    {
        var dictionary = BuildMetrics(theme);
        if (theme is null) return dictionary;

        // The shared AXAML dictionary supplies any icon a theme leaves unspecified.
        foreach (var key in ThemeKeyCatalog.Icons)
            if (theme.Resources.TryGetValue(key.Name, out var value))
                dictionary.Add(key.Name, value);

        return dictionary;
    }
}
