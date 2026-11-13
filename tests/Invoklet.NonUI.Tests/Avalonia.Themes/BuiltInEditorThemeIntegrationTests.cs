using Avalonia.Media;
using Invoklet.Avalonia.Themes;
using MarkView.Avalonia.SyntaxHighlighting;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.NonUI.Tests.Avalonia.Themes;

[Trait("Category", "Integration")]
[Trait("Platform", "Independent")]
public sealed class BuiltInEditorThemeIntegrationTests
{
    [Theory]
    [InlineData(ThemeName.DarkPlus)]
    [InlineData(ThemeName.Monokai)]
    [InlineData(ThemeName.Abbys)]
    [InlineData(ThemeName.KimbieDark)]
    [InlineData(ThemeName.TomorrowNightBlue)]
    [InlineData(ThemeName.HighContrastDark)]
    [InlineData(ThemeName.SolarizedDark)]
    [InlineData(ThemeName.AtomOneDark)]
    [InlineData(ThemeName.VisualStudioDark)]
    [InlineData(ThemeName.Light)]
    [InlineData(ThemeName.QuietLight)]
    [InlineData(ThemeName.SolarizedLight)]
    [InlineData(ThemeName.AtomOneLight)]
    [InlineData(ThemeName.VisualStudioLight)]
    [InlineData(ThemeName.HighContrastLight)]
    public void Highlight_BuiltInTheme_HasTheSameTokensAndColorsViaRawTheme(ThemeName name)
    {
        string[] sample = ["// sample", "class Example { string Title = \"hello\";", "    int Count => 42; }"];
        var expected = Snapshot(new TextMateHighlighter(name), sample);

        var actual = Snapshot(new TextMateHighlighter(EditorTheme.GetBuiltIn(name)), sample);

        Assert.NotEmpty(actual);
        Assert.Contains(actual, token => token.Color is not null);
        Assert.Equal(expected, actual);
    }

    private static (string Text, Color? Color)[] Snapshot(TextMateHighlighter highlighter, string[] sample) =>
        highlighter.HighlightLines(sample, "csharp").OfType<IReadOnlyList<(string Text, IBrush? Foreground)>>()
            .SelectMany(line => line).Select(token => (token.Text, (token.Foreground as ISolidColorBrush)?.Color)).ToArray();
}
