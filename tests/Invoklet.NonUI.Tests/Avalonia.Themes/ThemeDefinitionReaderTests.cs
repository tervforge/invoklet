using Avalonia;
using Avalonia.Media;
using Invoklet.Avalonia.Themes;
using Xunit;

namespace Invoklet.NonUI.Tests.Avalonia.Themes;

[Trait("Platform", "Independent")]
public sealed class ThemeDefinitionReaderTests
{
    private readonly ThemeDefinitionReaderFixture _fixture = new();

    [Fact]
    public void Read_AllResourceTypes_ReturnsTypedValuesAndEditorMetadata()
    {
        var reader = _fixture.CreateSut();
        var result = reader.Read(ThemeDefinitionReaderFixture.Complete);

        Assert.Empty(result.Diagnostics);
        var theme = Assert.IsType<ThemeDefinition>(result.Definition);
        Assert.Equal("example.theme", theme.Id);
        Assert.Equal("Example", theme.Name);
        Assert.Equal(ThemeBase.Light, theme.Base);
        Assert.Equal("example.parent", theme.Extends);
        Assert.Equal(Color.FromRgb(32, 48, 64), theme.Resources["Invoklet.Panel.BackgroundBrush"]);
        Assert.Equal(32d, theme.Resources["Invoklet.Size.Control"]);
        Assert.Equal(new Thickness(1, 2, 3, 4), theme.Resources["Invoklet.Dialog.Padding"]);
        Assert.Equal(new CornerRadius(1, 2, 3, 4), theme.Resources["Invoklet.Radius.Control"]);
        Assert.NotEmpty(Assert.IsType<PathGeometry>(theme.Resources["Icon.Play"]).Figures!);
        Assert.Equal("example-editor", theme.Editor!.Default);
        Assert.Equal("editor/example.json", theme.Editor.Themes["example-editor"]);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"id":"theme","id":"other","name":"Example"}""")]
    [InlineData("""{"id":"theme","name":"Example","colors":{"custom":1,"custom":2}}""")]
    [InlineData("""{"id":"theme","name":"\uD800"}""")]
    public void Read_InvalidDocument_ReportsDiagnosticAndReturnsNoDefinition(string json)
    {
        var reader = _fixture.CreateSut();

        var result = reader.Read(json);

        Assert.Null(result.Definition);
        Assert.Equal(ThemeDiagnosticCode.InvalidDocument, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("""{"name":"Example"}""", "id")]
    [InlineData("""{"id":"example","name":" "}""", "name")]
    [InlineData("""{"id":"example","name":"Example","base":"blue"}""", "base")]
    [InlineData("""{"id":"example","name":"Example","extends":1}""", "extends")]
    public void Read_InvalidIdentityOrInheritanceField_ReturnsNoDefinition(string json, string field)
    {
        var reader = _fixture.CreateSut();

        var result = reader.Read(json);

        Assert.Null(result.Definition);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ThemeDiagnosticCode.InvalidField, diagnostic.Code);
        Assert.Equal(field, diagnostic.Field);
    }

    [Theory]
    [InlineData("colors", "Invoklet.Panel.BackgroundBrush", "123")]
    [InlineData("colors", "Invoklet.Panel.BackgroundBrush", "\"bogus-color\"")]
    [InlineData("metrics", "Invoklet.Size.Control", "1e9999")]
    [InlineData("metrics", "Invoklet.Type.Body", "0")]
    [InlineData("metrics", "Invoklet.Size.Control", "-1")]
    [InlineData("metrics", "Invoklet.Space.Control", "1000000")]
    [InlineData("metrics", "Invoklet.Padding.IconButton", "[-1, 4]")]
    [InlineData("metrics", "Invoklet.Dialog.Padding", "[1, 2, 3]")]
    [InlineData("metrics", "Invoklet.Radius.Control", "[1, -2, 3, 4]")]
    [InlineData("icons", "Icon.Play", "\"M nope\"")]
    public void Read_InvalidResourceValue_OmitsOnlyThatOverride(string section, string key, string value)
    {
        var reader = _fixture.CreateSut();
        var json = _fixture.WithResource(section, key, value);

        var result = reader.Read(json);

        Assert.Empty(result.Definition!.Resources);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ThemeDiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal($"{section}.{key}", diagnostic.Field);
        Assert.True(diagnostic.IsError);
    }

    [Fact]
    public void Read_UnknownKey_ReportsWarningAndKeepsTheDefinition()
    {
        var reader = _fixture.CreateSut();
        var json = _fixture.WithResource("colors", "Invoklet.Future.Brush", "\"#203040\"");

        var result = reader.Read(json);

        Assert.Empty(result.Definition!.Resources);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ThemeDiagnosticCode.UnknownKey, diagnostic.Code);
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void Read_KnownKeyInAnotherSection_ReportsItsExpectedSection()
    {
        var reader = _fixture.CreateSut();
        var json = _fixture.WithResource("colors", "Invoklet.Size.Control", "32");

        var result = reader.Read(json);

        Assert.Empty(result.Definition!.Resources);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ThemeDiagnosticCode.InvalidField, diagnostic.Code);
        Assert.Contains("'metrics'", diagnostic.Message);
    }

    [Fact]
    public void Read_MalformedEditorMetadata_KeepsValidEditorEntries()
    {
        var reader = _fixture.CreateSut();

        var result = reader.Read(ThemeDefinitionReaderFixture.MalformedEditor);

        var editor = result.Definition!.Editor!;
        Assert.Null(editor.Default);
        Assert.Equal("editor/good.json", Assert.Single(editor.Themes).Value);
        Assert.Equal(["editor.default", "editor.themes.bad"], result.Diagnostics.Select(item => item.Field));
    }
}

internal sealed class ThemeDefinitionReaderFixture
{
    public const string Complete = """
        {
          "id": "example.theme",
          "name": "Example",
          "base": "light",
          "extends": "example.parent",
          "colors": { "Invoklet.Panel.BackgroundBrush": "#203040" },
          "metrics": {
            "Invoklet.Size.Control": 32,
            "Invoklet.Dialog.Padding": [1, 2, 3, 4],
            "Invoklet.Radius.Control": [1, 2, 3, 4]
          },
          "icons": { "Icon.Play": "M8,5L8,19L19,12Z" },
          "editor": { "default": "example-editor", "themes": { "example-editor": "editor/example.json" } }
        }
        """;

    public const string MalformedEditor = """
        {
          "id": "example.theme",
          "name": "Example",
          "editor": { "default": 1, "themes": { "bad": false, "good": "editor/good.json" } }
        }
        """;

    public ThemeDefinitionReader CreateSut() => new();

    public string WithResource(string section, string key, string value) =>
        $$"""{"id":"example.theme","name":"Example","{{section}}":{"{{key}}":{{value}}} }""";
}
