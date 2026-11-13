using System.Text;
using Avalonia.Media;
using Invoklet.Avalonia.Themes;
using MarkView.Avalonia.SyntaxHighlighting;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.NonUI.Tests.Avalonia.Themes;

[Trait("Platform", "Independent")]
public sealed class EditorThemeReaderTests
{
    private readonly EditorThemeReaderFixture _fixture = new();

    [Fact]
    public void Read_StandaloneTheme_ColorsCSharpKeywords()
    {
        var reader = _fixture.CreateSut();
        using var asset = _fixture.Asset(EditorThemeReaderFixture.Custom);

        var result = reader.Read(asset, "Custom", _fixture.Fallback);

        Assert.Null(result.Diagnostic);
        var tokens = new TextMateHighlighter(result.Theme.RawTheme).Highlight("class Sample { }".AsMemory(), "csharp");
        var keyword = Assert.Single(tokens!, token => token.Text == "class");
        Assert.Equal(Color.FromRgb(19, 181, 138), Assert.IsAssignableFrom<ISolidColorBrush>(keyword.Foreground).Color);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"tokenColors":[42]}""")]
    [InlineData("""{"include":"other.json","tokenColors":[]}""")]
    [InlineData("""{"tokenColors":"other.json"}""")]
    public void Read_InvalidAsset_ReturnsFallbackAndDiagnostic(string json)
    {
        var reader = _fixture.CreateSut();
        using var asset = _fixture.Asset(json);

        var result = reader.Read(asset, "Invalid", _fixture.Fallback);

        Assert.Same(_fixture.Fallback, result.Theme);
        Assert.NotNull(result.Diagnostic);
    }
}

internal sealed class EditorThemeReaderFixture
{
    public const string Custom = """
        {
          // VS Code theme JSON allows comments and trailing commas.
          "name": "Custom",
          "tokenColors": [
            { "scope": "storage.type", "settings": { "foreground": "#13B58A" } },
          ],
        }
        """;

    public EditorTheme Fallback { get; } = new("Dark+", ThemeName.DarkPlus, true);
    public EditorThemeReader CreateSut() => new();
    public MemoryStream Asset(string json) => new(Encoding.UTF8.GetBytes(json));
}
