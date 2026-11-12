using Avalonia.Styling;
using Invoklet.Avalonia.Themes;
using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public class CodeOutputHighlighterTests
{
    [Fact]
    public void Highlight_adds_coloured_tokens_to_a_code_block()
    {
        var html = CodeOutputHighlighter.Highlight(
            "<pre class=\"dp-code\" data-language=\"csharp\">var x = 1;</pre>");

        Assert.Contains("<div class=\"dp-code-shell\">", html);
        Assert.Contains("<a class=\"dp-code-copy\"", html);
        Assert.Matches("<span style=\"color:#[0-9A-F]{6}\">var</span>", html);
        Assert.Matches("<span style=\"color:#[0-9A-F]{6}\">1</span>", html);
    }

    [Fact]
    public void Highlight_preserves_different_token_colours()
    {
        var html = CodeOutputHighlighter.Highlight(
            "<pre class=\"dp-code\" data-language=\"csharp\">var result = await service.LoadAsync();</pre>");

        Assert.Contains(">var</span>", html);
        Assert.Contains(">await</span>", html);
        Assert.Contains("LoadAsync", html);
        Assert.Contains("dp-code-", html);
    }

    [Fact]
    public void Highlight_UsesTheLightPaletteForAnInvokletLightTheme()
    {
        const string html = "<pre class=\"dp-code\" data-language=\"csharp\">var x = 1;</pre>";
        var light = CodeOutputHighlighter.Highlight(html, ThemeVariant.Light);

        Assert.NotEqual(CodeOutputHighlighter.Highlight(html, ThemeVariant.Dark), light);
        Assert.Equal(light, CodeOutputHighlighter.Highlight(html, InvokletThemeVariants.VsCodeLight));
    }

    [Theory]
    [InlineData("sql", "SELECT 1;")]
    [InlineData("xml", "<root />")]
    [InlineData("json", "{ \"enabled\": true }")]
    public void Highlight_keeps_language_and_renders_supported_code(string language, string source)
    {
        var html = CodeOutputHighlighter.Highlight(
            $"<pre class=\"dp-code\" data-language=\"{language}\">{System.Net.WebUtility.HtmlEncode(source)}</pre>");

        Assert.Contains($"data-language=\"{language}\"", html);
        Assert.Matches("<span style=\"color:#[0-9A-F]{6}\">", html);
    }

    [Fact]
    public void Highlight_keeps_plaintext_without_inventing_language_tokens()
    {
        var html = CodeOutputHighlighter.Highlight(
            "<pre class=\"dp-code\" data-language=\"plaintext\">plain text</pre>");

        Assert.Contains("data-language=\"plaintext\"", html);
        Assert.Contains("plain text", html);
        Assert.DoesNotContain("<span", html);
    }

    [Fact]
    public void Highlight_escapes_code_and_encodes_the_copy_payload()
    {
        const string source = "if (x < 2) return;";
        var html = CodeOutputHighlighter.Highlight(
            "<pre class=\"dp-code\" data-language=\"csharp\">if (x &lt; 2) return;</pre>");

        Assert.Contains("&lt;", html);
        var encodedCopy = System.Net.WebUtility.UrlEncode(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Contains($"href=\"invoklet-copy:{encodedCopy}#0\"", html);
    }

    [Fact]
    public void Highlight_keeps_multiline_comment_state_within_each_block()
    {
        var html = CodeOutputHighlighter.Highlight(
            "<pre class=\"dp-code\" data-language=\"csharp\">/* start\nvar continued\n*/</pre>" +
            "<pre class=\"dp-code\" data-language=\"csharp\">var outside = 1;</pre>");

        Assert.Contains("<span style=\"color:#6A9955\">var continued</span>", html);
        Assert.Matches("<span style=\"color:#[0-9A-F]{6}\">var</span>", html);
    }
}
