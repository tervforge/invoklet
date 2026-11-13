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

    public static InvokletTheme CreateStyle() => new(Bundled.Value);

    private static IReadOnlyList<InterfaceTheme> Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Invoklet.sln")))
            directory = directory.Parent;
        var root = Path.Combine(directory!.FullName, "build", "plugins");
        var packages = Directory.GetDirectories(Path.Combine(root, "themes"));
        var catalog = new ThemeCatalog();
        var session = new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = PluginStartup.ApplicationVersion,
            BundledDirectory = root,
            BundledCatalog = new BundledPluginCatalog(packages.Select(path =>
                new BundledPluginEntry(Path.GetFileName(path), "1.0.0", PluginContent.ComputeHash(path)))),
            NativeLibraryCacheDirectory = Path.Combine(root, "unused-native-cache"),
        }, [catalog], NullLoggerFactory.Instance).Load();
        if (session.Rejected.Count != 0 || catalog.Interfaces.Count != 6)
            throw new InvalidOperationException("Could not load bundled test themes.");
        return catalog.Interfaces;
    }
}
