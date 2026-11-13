using System.Text.RegularExpressions;
using System.Xml.Linq;
using Invoklet.Avalonia.Themes;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Category", "Integration")]
[Trait("Platform", "Independent")]
public partial class ThemeResourceKeyParityTests
{
    private const string ThemeDirectory = "src/controls/Invoklet.Avalonia.Themes";
    private const string AppDirectory = "src/main/Invoklet";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    // These references resolve through Avalonia, Fluent or the platform, outside Invoklet's dictionaries.
    private static readonly HashSet<string> ExternalKeys = new(StringComparer.Ordinal)
    {
        "ContentControlThemeFontFamily",
        "ControlContentThemeFontSize",
        "SystemAccentColor",
        "SystemControlBackgroundAltHighBrush",
        "SystemControlForegroundBaseHighBrush",
        "SystemRegionBrush",
    };

    private readonly string _root = FindRepoRoot();

    [Fact]
    public void BuiltInPalettes_LoadedFromJson_DefineEveryCataloguedPaletteKey()
    {
        var builtIn = Directory.GetFiles(Path.Combine(_root, ThemeDirectory, "Themes"), "*.json");
        var bundled = Directory.GetFiles(Path.Combine(_root, "build", "plugins", "themes"), "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "invoklet-plugin.json").ToArray();
        var files = builtIn.Concat(bundled);
        var reader = new ThemeDefinitionReader();
        var expected = ThemeKeyCatalog.Palette.Select(key => key.Name).Order(StringComparer.Ordinal).ToArray();
        var ids = new List<string>();
        foreach (var file in files)
        {
            var result = reader.Read(File.ReadAllText(file));
            Assert.Empty(result.Diagnostics);
            var definition = Assert.IsType<ThemeDefinition>(result.Definition);
            Assert.Equal(expected, definition.Resources.Keys.Order(StringComparer.Ordinal));
            ids.Add(definition.Id);
        }

        Assert.Equal(8, ids.Count);
        Assert.Equal(8, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(6, bundled.Length);
        Assert.Equal(2, BuiltInInterfaceThemes.All.Count);
        Assert.All(BuiltInInterfaceThemes.All, theme => Assert.Contains(theme.Id, ids));
    }

    [Fact]
    public void SharedMetrics_LoadedFromJson_DefineEveryCataloguedMetricAndAlias()
    {
        var reader = new ThemeDefinitionReader();
        var defaults = reader.Read(File.ReadAllText(Path.Combine(_root, ThemeDirectory, "Metrics.json")));
        var keys = ThemeKeyCatalog.Metrics;

        var resources = ThemeBuilder.BuildMetrics();

        Assert.Empty(defaults.Diagnostics);
        Assert.Equal(keys.Where(key => key.AliasFor is null).Select(key => key.Name).Order(StringComparer.Ordinal),
            defaults.Definition!.Resources.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(keys.Select(key => key.Name).Order(StringComparer.Ordinal),
            resources.Keys.Cast<string>().Order(StringComparer.Ordinal));
        foreach (var key in keys)
            Assert.Equal(defaults.Definition.Resources[key.AliasFor ?? key.Name], resources[key.Name]);
    }

    [Fact]
    public void SharedAndExplorerIcons_DeclaredInAxaml_MatchTheCatalog()
    {
        var shared = Load(ThemeDirectory + "/Icons.axaml").Root!.Elements();
        var keys = shared.Where(element => element.Name == Avalonia + "StreamGeometry")
            .Select(element => element.Attribute(Xaml + "Key")!.Value).Order(StringComparer.Ordinal);
        Assert.Equal(ThemeKeyCatalog.Icons.Select(key => key.Name).Order(StringComparer.Ordinal), keys);
    }

    [Fact]
    public void IconReferences_InApplicationAxaml_UseDynamicResources()
    {
        var staticIcons = Directory.EnumerateFiles(Path.Combine(_root, AppDirectory), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => StaticIconReference().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(_root, path)).ToArray();

        Assert.Empty(staticIcons);
    }

    [GeneratedRegex("""\{StaticResource\s+(?:ResourceKey\s*=\s*)?(?:Icon|AsmIcon|AsmOverlay)\.""")]
    private static partial Regex StaticIconReference();

    [Fact]
    public void ThemeResourceReferences_InAppAndControlAxaml_ResolveToKnownKeys()
    {
        string[] roots = [AppDirectory, ThemeDirectory, "src/controls/MarkView.Avalonia"];
        var documents = roots.SelectMany(root => Directory.EnumerateFiles(Path.Combine(_root, root), "*.axaml", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(_root, path))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => (Path: path, Document: Load(path)))
            .ToArray();
        var localKeys = documents.SelectMany(item => item.Document.Descendants().Attributes(Xaml + "Key"))
            .Where(attribute => !IsThemeKey(attribute.Value)
                || attribute.Parent?.Name.LocalName is "ControlTheme" or "FocusAdornerTemplate")
            .Select(attribute => attribute.Value).ToHashSet(StringComparer.Ordinal);

        var unknown = documents.SelectMany(item => item.Document.Descendants().Attributes()
            .SelectMany(attribute => ReferencedKeys(attribute).Select(key => (item.Path, Key: key))))
            .Where(item => !ThemeKeyCatalog.All.ContainsKey(item.Key) && !ExternalKeys.Contains(item.Key)
                && !localKeys.Contains(item.Key))
            .Distinct().OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Path}: {item.Key}")
            .ToArray();

        Assert.True(unknown.Length == 0, string.Join(Environment.NewLine, unknown));
    }

    private static bool IsThemeKey(string key) =>
        key.StartsWith("Invoklet.", StringComparison.Ordinal) || key.StartsWith("Icon.", StringComparison.Ordinal)
        || key.StartsWith("AsmIcon.", StringComparison.Ordinal) || key.StartsWith("AsmOverlay.", StringComparison.Ordinal);

    private static IEnumerable<string> ReferencedKeys(XAttribute attribute)
    {
        foreach (Match match in ResourceReference().Matches(attribute.Value))
            yield return match.Groups["key"].Value;
        if (attribute.Name.LocalName == "ResourceKey" && attribute.Parent?.Name.LocalName is "StaticResource" or "DynamicResource")
            yield return attribute.Value;
    }

    [GeneratedRegex("""\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s{},]+)""")]
    private static partial Regex ResourceReference();

    private XDocument Load(string path) => XDocument.Load(Path.Combine(_root, path));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Invoklet.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Invoklet.sln from " + AppContext.BaseDirectory);
    }
}
