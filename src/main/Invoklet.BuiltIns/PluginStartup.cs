using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Microsoft.Extensions.Logging;

namespace Invoklet.BuiltIns;

public static class PluginStartup
{
    private static readonly IReadOnlyDictionary<string, string> ReplacedThemeIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["invoklet.theme.vscode-dark"] = "invoklet.theme.vscode",
        ["invoklet.theme.vscode-light"] = "invoklet.theme.vscode",
        ["invoklet.theme.rider-dark"] = "invoklet.theme.rider",
        ["invoklet.theme.rider-light"] = "invoklet.theme.rider",
        ["invoklet.theme.vs2022-dark"] = "invoklet.theme.vs2022",
        ["invoklet.theme.vs2022-light"] = "invoklet.theme.vs2022",
    };

    public static Version ApplicationVersion { get; } = typeof(PluginStartup).Assembly.GetName().Version!;

    public static PluginDevelopmentOptions ParseArguments(IReadOnlyList<string> arguments) =>
        PluginDevelopmentOptions.Parse(arguments, Environment.GetEnvironmentVariable(PluginDevelopmentOptions.EnvironmentVariable));

    private static string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet");

    private static PluginInventoryStore InventoryStore => new(Path.Combine(UserDataDirectory, PluginInventory.FileName));

    public static PluginInventory ReadUserInventory() =>
        InventoryStore.Read(ReadBundledCatalog(BundledDirectory()), ReplacedThemeIds);

    public static void SetPluginEnabled(string id, bool enabled, PluginSession session)
    {
        var roles = session.Find(id)?.Manifest.Roles
            ?? session.Rejected.FirstOrDefault(plugin => plugin.Id == id && plugin.Trust == PluginTrust.Bundled)?.Manifest?.Roles;
        InventoryStore.SetEnabled(id, enabled, ReadBundledCatalog(BundledDirectory()), ReplacedThemeIds,
            roles?.Keys.Select(PluginRoles.Name).ToArray(), session, Path.Combine(UserDataDirectory, "plugins"));
    }

    public static PluginPackageInstaller CreatePluginInstaller() =>
        new(Path.Combine(UserDataDirectory, "plugins"), InventoryStore, ReadBundledCatalog(BundledDirectory()));

    private static string BundledDirectory()
    {
        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (OperatingSystem.IsMacOS())
        {
            var application = new DirectoryInfo(AppContext.BaseDirectory);
            if (application.Name == "MacOS" && application.Parent is { Name: "Contents" } contents)
                bundledDirectory = Path.Combine(contents.FullName, "Resources", "plugins");
        }
        return bundledDirectory;
    }

    private static BundledPluginCatalog ReadBundledCatalog(string directory) =>
        BundledPluginCatalog.Read(Path.Combine(directory, BundledPluginCatalog.FileName));

    public static PluginSession Load(string? developmentDirectory, ILoggerFactory loggerFactory,
        IReadOnlyList<IPluginRoleHandler>? roleHandlers = null)
    {
        var bundledDirectory = BundledDirectory();
        var bundledCatalog = ReadBundledCatalog(bundledDirectory);
        return new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = ApplicationVersion,
            BundledDirectory = bundledDirectory,
            BundledCatalog = bundledCatalog,
            UserDirectory = Path.Combine(UserDataDirectory, "plugins"),
            Inventory = InventoryStore.Read(bundledCatalog, ReplacedThemeIds),
            DevelopmentDirectory = developmentDirectory,
            LoadThemePackages = roleHandlers?.Any(handler => handler.Role == Invoklet.Plugins.PluginRole.Theme) == true,
            // Machine-local: copies of native libraries are neither roamed nor kept in a temporary folder.
            NativeLibraryCacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invoklet", "plugin-native"),
        }, roleHandlers ?? [], loggerFactory).Load();
    }
}
