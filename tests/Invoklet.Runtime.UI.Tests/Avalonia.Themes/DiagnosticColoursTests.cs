using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Invoklet.Avalonia.Themes;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.Avalonia.Themes;

// The colours of the editor's underlines and the marks beside its scroll bar: every theme has its own, and they read on its editor.
public sealed class DiagnosticColoursTests
{
    public static TheoryData<string> Themes =>
        new(typeof(InvokletThemeVariants).GetFields().Where(f => f.FieldType == typeof(ThemeVariant)).Select(f => f.Name));

    private static Color ColourOf(InvokletTheme theme, ThemeVariant variant, string key)
    {
        Assert.True(theme.TryGetResource(key, variant, out var value), $"{key} missing");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) => v / 255d <= 0.03928 ? v / 255d / 12.92 : Math.Pow((v / 255d + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (light, dark) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (light + 0.05) / (dark + 0.05);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void DiagnosticColours_OnTheEditorOfATheme_ReadAndDiffer(string themeName)
    {
        // Arrange
        var variant = (ThemeVariant)typeof(InvokletThemeVariants).GetField(themeName)!.GetValue(null)!;
        var theme = new InvokletTheme();

        // Act
        var background = ColourOf(theme, variant, "Invoklet.Editor.BackgroundBrush");
        Color[] colours = [.. new[] { "Error", "Warning", "Info" }.Select(k => ColourOf(theme, variant, $"Invoklet.Diagnostic.{k}Brush"))];

        // Assert
        Assert.All(colours, c => Assert.True(Contrast(c, background) >= 3, $"{themeName}: {c} on {background} is {Contrast(c, background):F2}"));
        Assert.Equal(3, colours.Distinct().Count());
    }
}
