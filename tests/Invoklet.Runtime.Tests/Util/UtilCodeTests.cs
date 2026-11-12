using Invoklet.Runtime;
using Xunit;

namespace Invoklet.Runtime.Tests;

public class UtilCodeTests
{
    [Fact]
    public void Code_renders_escaped_source_in_a_language_aware_block()
    {
        var html = DumpRenderer.Render(global::Util.Code("var x = 1 < 2;", CodeLanguage.CSharp), null);

        Assert.Contains("class=\"dp-code\"", html);
        Assert.Contains("data-language=\"csharp\"", html);
        Assert.Contains("var x = 1 &lt; 2;", html);
    }

    [Fact]
    public void Code_renders_json_language()
    {
        var html = DumpRenderer.Render(global::Util.Code("{\"ok\":true}", CodeLanguage.Json), null);

        Assert.Contains("data-language=\"json\"", html);
    }

    [Fact]
    public void Code_renders_xml_language()
    {
        var html = DumpRenderer.Render(global::Util.Code("<root />", CodeLanguage.Xml), null);

        Assert.Contains("data-language=\"xml\"", html);
    }

    [Theory]
    [InlineData(CodeLanguage.PlainText, "plaintext")]
    [InlineData(CodeLanguage.CSharp, "csharp")]
    [InlineData(CodeLanguage.Sql, "sql")]
    [InlineData(CodeLanguage.Json, "json")]
    [InlineData(CodeLanguage.Xml, "xml")]
    public void Code_maps_every_language_to_the_renderer_language_id(CodeLanguage language, string languageId)
    {
        var html = DumpRenderer.Render(global::Util.Code("source", language), null);

        Assert.Contains($"data-language=\"{languageId}\"", html);
    }

    [Fact]
    public void Code_rejects_a_null_source()
        => Assert.Throws<ArgumentNullException>(() => global::Util.Code(null!));

    [Fact]
    public void Code_defaults_to_csharp_for_existing_call_sites()
    {
        var html = DumpRenderer.Render(global::Util.Code("var value = 1;"), null);

        Assert.Contains("data-language=\"csharp\"", html);
    }
}
