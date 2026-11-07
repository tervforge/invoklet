using Avalonia;
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

    // Every table, at any depth: a blue title strip and border, grey column headers, each with text that reads on it.
    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void TableColours_OfATheme_AreABlueStripAndGreyColumnHeaders(string themeName)
    {
        // Arrange
        var variant = (ThemeVariant)typeof(InvokletThemeVariants).GetField(themeName)!.GetValue(null)!;
        var theme = new InvokletTheme();

        // Act
        var strip = ColourOf(theme, variant, "Invoklet.Dump.TableHeaderBrush");
        var column = ColourOf(theme, variant, "Invoklet.Dump.ColumnHeaderBrush");
        var columnText = ColourOf(theme, variant, "Invoklet.Dump.ColumnHeaderForegroundBrush");

        // Assert
        Assert.True(strip.B > strip.R, $"{themeName}: {strip} is not blue");
        Assert.True(Math.Max(column.R, Math.Max(column.G, column.B)) - Math.Min(column.R, Math.Min(column.G, column.B)) <= 16, $"{themeName}: {column} is not grey");
        Assert.True(Contrast(columnText, column) >= 4.5, $"{themeName}: {columnText} on {column}");
    }

    // The sheet the panel reads: column headers take the grey, title strips and borders the blue.
    [AvaloniaFact]
    public void Stylesheet_OfATable_ColoursItsPartsFromTheirOwnBrushes()
    {
        // Arrange
        var variant = InvokletThemeVariants.RiderDark;
        var app = Application.Current!;
        var theme = new InvokletTheme();
        var requested = app.RequestedThemeVariant;
        app.Styles.Add(theme);
        app.RequestedThemeVariant = variant;

        // Act
        string css;
        try { css = HtmlThemeStylesheet.Generate(variant, isDark: true); }
        finally
        {
            app.Styles.Remove(theme);
            app.RequestedThemeVariant = requested;
        }

        // Assert
        Assert.Contains(".dp-th      { background-color:#43454A;", css);
        Assert.Contains(".dp-boxed      { border:2px solid #2D5FAD;", css);
        Assert.Contains(".dp-boxstrip   { padding:0; background-color:#2D5FAD;", css);
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
