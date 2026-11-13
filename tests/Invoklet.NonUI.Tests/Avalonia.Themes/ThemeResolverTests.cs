using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;
using Invoklet.Avalonia.Themes;
using Xunit;

namespace Invoklet.NonUI.Tests.Avalonia.Themes;

[Trait("Platform", "Independent")]
public sealed class ThemeResolverTests
{
    private readonly ThemeResolverFixture _fixture = new();

    [Fact]
    public void Resolve_TwoColorOverridesWithParents_FillsEveryCatalogKey()
    {
        var resolver = _fixture.CreateSut();
        var (theme, parents) = _fixture.LayeredThemes();

        var result = resolver.Resolve(theme, parents);

        Assert.Empty(result.Diagnostics);
        var resolved = Assert.IsType<ResolvedTheme>(result.Theme);
        Assert.Equal(ThemeBase.Light, resolved.Base);
        Assert.Equal(ThemeKeyCatalog.All.Keys.Order(StringComparer.Ordinal), resolved.Resources.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Colors.Green, resolved.Resources["Invoklet.Panel.BackgroundBrush"]);
        Assert.Equal(Colors.Orange, resolved.Resources["Invoklet.Text.PrimaryBrush"]);
        Assert.Equal(Colors.Red, resolved.Resources["Invoklet.Editor.BackgroundBrush"]);
        Assert.Equal(Colors.White, resolved.Resources["Invoklet.Sidebar.BackgroundBrush"]);
        Assert.Equal(new Thickness(4), resolved.Resources["Invoklet.Dialog.Padding"]);
        Assert.IsType<PathGeometry>(resolved.Resources["Icon.Play"]);
    }

    [Fact]
    public void Resolve_ChildSpecifiesBase_UsesThatPaletteForRemainingKeys()
    {
        var resolver = _fixture.CreateSut();
        var (theme, parents) = _fixture.LayeredThemes();

        var result = resolver.Resolve(theme with { Base = ThemeBase.Dark }, parents);

        Assert.Equal(ThemeBase.Dark, result.Theme!.Base);
        Assert.Equal(Colors.Black, result.Theme.Resources["Invoklet.Sidebar.BackgroundBrush"]);
    }

    [Fact]
    public void Resolve_DeepInheritance_UsesTheRootOverride()
    {
        var resolver = _fixture.CreateSut();
        var (theme, parents) = _fixture.DeepThemes();

        var result = resolver.Resolve(theme, parents);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(Colors.Red, result.Theme!.Resources["Invoklet.Panel.BackgroundBrush"]);
    }

    [Theory]
    [InlineData("leaf", ThemeDiagnosticCode.InheritanceCycle)]
    [InlineData("unknown", ThemeDiagnosticCode.MissingParent)]
    public void Resolve_InvalidParentChain_ReportsAnError(string rootParent, ThemeDiagnosticCode expected)
    {
        var resolver = _fixture.CreateSut();
        var (theme, parents) = _fixture.InvalidChain(rootParent);

        var result = resolver.Resolve(theme, parents);

        Assert.Null(result.Theme);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(expected, diagnostic.Code);
        Assert.True(diagnostic.IsError);
        Assert.Contains(rootParent, diagnostic.Message);
    }

    [Fact]
    public void Resolve_CatalogGainsAKey_OldThemeUsesItsNewBuiltInDefault()
    {
        var resolver = _fixture.CreateSutWithNewKey();
        var theme = _fixture.OldTheme();

        var result = resolver.Resolve(theme, ImmutableDictionary<string, ThemeDefinition>.Empty);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(Colors.AliceBlue, result.Theme!.Resources[ThemeResolverFixture.NewKey.Name]);
    }

    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 12)]
    public void Resolve_AliasAndCanonicalOverrides_KeepBothNamesInSync(bool canonical, int expected)
    {
        var resolver = _fixture.CreateSut();
        var theme = _fixture.AliasTheme(canonical);

        var result = resolver.Resolve(theme, ImmutableDictionary<string, ThemeDefinition>.Empty);

        Assert.Equal(new CornerRadius(expected), result.Theme!.Resources["Invoklet.Radius.Control"]);
        Assert.Equal(new CornerRadius(expected), result.Theme.Resources["ControlCornerRadius"]);
    }

    [Fact]
    public void Resolve_InheritedEditorAssets_KeepsEachAssetsOwningTheme()
    {
        var resolver = _fixture.CreateSut();
        var (theme, parents) = _fixture.EditorThemes();

        var result = resolver.Resolve(theme, parents);

        Assert.Equal("base-editor", result.Theme!.EditorDefault);
        Assert.Equal(new ThemeEditorAsset("parent", "editor/base.json"), result.Theme.EditorThemes["base-editor"]);
        Assert.Equal(new ThemeEditorAsset("leaf", "editor/override.json"), result.Theme.EditorThemes["shared"]);
    }
}

internal sealed class ThemeResolverFixture
{
    public static readonly ThemeKey NewKey = new("Invoklet.Future.Brush", ThemeKeyType.Color, "A later catalog entry");

    public ThemeResolver CreateSut() => new(CreateDefaults());

    public ThemeResolver CreateSutWithNewKey()
    {
        var defaults = CreateDefaults();
        return new ThemeResolver(defaults with { Dark = defaults.Dark.Add(NewKey.Name, Colors.AliceBlue) },
            ThemeKeyCatalog.All.ToImmutableDictionary().Add(NewKey.Name, NewKey));
    }

    public ThemeDefinition OldTheme() => Define("old");

    public ThemeDefinition AliasTheme(bool canonical)
    {
        var theme = Define("alias", resources: [("ControlCornerRadius", new CornerRadius(9))]);
        return canonical ? theme with { Resources = theme.Resources.Add("Invoklet.Radius.Control", new CornerRadius(12)) } : theme;
    }

    public (ThemeDefinition Theme, IReadOnlyDictionary<string, ThemeDefinition> Parents) LayeredThemes()
    {
        var root = Define("root", themeBase: ThemeBase.Light, resources:
            [("Invoklet.Editor.BackgroundBrush", Colors.Red), ("Invoklet.Panel.BackgroundBrush", Colors.Blue)]);
        var parent = Define("parent", "root", resources: [("Invoklet.Panel.BackgroundBrush", Colors.Yellow)]);
        var leaf = Define("leaf", "parent", resources:
            [("Invoklet.Panel.BackgroundBrush", Colors.Green), ("Invoklet.Text.PrimaryBrush", Colors.Orange)]);
        return (leaf, Parents(root, parent));
    }

    public (ThemeDefinition Theme, IReadOnlyDictionary<string, ThemeDefinition> Parents) DeepThemes()
    {
        var root = Define("root", resources: [("Invoklet.Panel.BackgroundBrush", Colors.Red)]);
        var parents = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal) { [root.Id] = root };
        var last = root;
        for (var i = 0; i < 1024; i++)
        {
            last = Define($"ancestor-{i}", last.Id);
            parents.Add(last.Id, last);
        }

        return (Define("leaf", last.Id), parents);
    }

    public (ThemeDefinition Theme, IReadOnlyDictionary<string, ThemeDefinition> Parents) InvalidChain(string rootParent) =>
        (Define("leaf", "parent"), Parents(Define("parent", "root"), Define("root", rootParent)));

    public (ThemeDefinition Theme, IReadOnlyDictionary<string, ThemeDefinition> Parents) EditorThemes()
    {
        var parent = Define("parent") with
        {
            Editor = new ThemeEditorDefinition("base-editor", ImmutableDictionary<string, string>.Empty
                .Add("base-editor", "editor/base.json").Add("shared", "editor/shared.json")),
        };
        var child = Define("leaf", "parent") with
        {
            Editor = new ThemeEditorDefinition(null, ImmutableDictionary<string, string>.Empty.Add("shared", "editor/override.json")),
        };
        return (child, Parents(parent));
    }

    private static ThemeDefaults CreateDefaults() =>
        new(Palette(Colors.Black), Palette(Colors.White), ThemeKeyCatalog.Metrics.Concat(ThemeKeyCatalog.Icons)
            .Where(key => key.AliasFor is null).ToImmutableDictionary(key => key.Name, key => DefaultValue(key, Colors.Black)));

    private static ImmutableDictionary<string, object> Palette(Color color) =>
        ThemeKeyCatalog.Palette.ToImmutableDictionary(key => key.Name, key => DefaultValue(key, color));

    private static object DefaultValue(ThemeKey key, Color color) => key.Type switch
    {
        ThemeKeyType.Color => color,
        ThemeKeyType.Double => 4d,
        ThemeKeyType.Thickness => new Thickness(4),
        ThemeKeyType.CornerRadius => new CornerRadius(4),
        ThemeKeyType.Geometry => new PathGeometry(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static ThemeDefinition Define(string id, string? extends = null, ThemeBase? themeBase = null,
        (string Key, object Value)[]? resources = null) =>
        new(id, id, themeBase, extends, (resources ?? []).ToImmutableDictionary(item => item.Key, item => item.Value));

    private static IReadOnlyDictionary<string, ThemeDefinition> Parents(params ThemeDefinition[] parents) =>
        parents.ToDictionary(theme => theme.Id, StringComparer.Ordinal);
}
