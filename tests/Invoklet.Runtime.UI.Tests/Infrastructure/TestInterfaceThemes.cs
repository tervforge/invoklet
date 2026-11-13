using Invoklet.Avalonia.Themes;
using Invoklet.BuiltIns;
using Invoklet.Plugins.Hosting;
using Invoklet.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.Runtime.UI.Tests.Infrastructure;

internal static class TestInterfaceThemes
{
    private static readonly Lazy<IReadOnlyList<InterfaceTheme>> Bundled = new(Load);

    public static IReadOnlyList<InterfaceTheme> All => [.. BuiltInInterfaceThemes.All, .. Bundled.Value];

    public static global::Avalonia.Styling.ThemeVariant GetVariant(string id) => All.Single(theme => theme.Id == id).Variant;

    public static InvokletTheme CreateStyle()
    {
        var style = new InvokletTheme();
        foreach (var theme in Bundled.Value)
            style.Resources.ThemeDictionaries.Add(theme.Variant, ThemeBuilder.BuildPalette(theme.Definition!));
        return style;
    }

    private static IReadOnlyList<InterfaceTheme> Load()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        var catalog = new ThemeCatalog();
        var session = new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = PluginStartup.ApplicationVersion,
            BundledDirectory = root,
            BundledCatalog = BundledPluginCatalog.Read(Path.Combine(root, BundledPluginCatalog.FileName)),
            NativeLibraryCacheDirectory = Path.Combine(root, "unused-native-cache"),
        }, [catalog], NullLoggerFactory.Instance).Load();
        if (session.Rejected.Count != 0 || catalog.Interfaces.Count != 6)
            throw new InvalidOperationException("Could not load bundled test themes.");
        return catalog.Interfaces;
    }
}
