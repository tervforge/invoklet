using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Invoklet.Avalonia.Themes;

public class InvokletTheme : Styles
{
    public InvokletTheme()
        : this([]) { }

    public InvokletTheme(IEnumerable<InterfaceTheme> additionalThemes)
    {
        AvaloniaXamlLoader.Load(this);
        Resources.MergedDictionaries.Add(ThemeBuilder.BuildMetrics());
        BuiltInInterfaceThemes.RegisterPalettes(Resources);
        foreach (var theme in additionalThemes)
            if (theme.Definition is { } definition)
                Resources.ThemeDictionaries.Add(theme.Variant, ThemeBuilder.BuildPalette(definition));
    }
}
