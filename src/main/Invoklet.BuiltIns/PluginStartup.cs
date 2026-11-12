using Invoklet.Plugins.Hosting;
using Microsoft.Extensions.Logging;

namespace Invoklet.BuiltIns;

public static class PluginStartup
{
    public static Version ApplicationVersion { get; } = typeof(PluginStartup).Assembly.GetName().Version!;

    public static PluginDevelopmentOptions ParseArguments(IReadOnlyList<string> arguments) =>
        PluginDevelopmentOptions.Parse(arguments, Environment.GetEnvironmentVariable(PluginDevelopmentOptions.EnvironmentVariable));

    private static string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet");

    public static PluginInventory ReadUserInventory() => PluginInventory.Read(Path.Combine(UserDataDirectory, PluginInventory.FileName));

    public static PluginSession Load(string? developmentDirectory, ILoggerFactory loggerFactory)
    {
        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (OperatingSystem.IsMacOS())
        {
            var application = new DirectoryInfo(AppContext.BaseDirectory);
            if (application.Name == "MacOS" && application.Parent is { Name: "Contents" } contents)
                bundledDirectory = Path.Combine(contents.FullName, "Resources", "plugins");
        }
        return new PluginLoader(new PluginLoaderOptions
        {
            ApplicationVersion = ApplicationVersion,
            BundledDirectory = bundledDirectory,
            BundledCatalog = BundledPluginCatalog.Read(Path.Combine(bundledDirectory, BundledPluginCatalog.FileName)),
            UserDirectory = Path.Combine(UserDataDirectory, "plugins"),
            Inventory = ReadUserInventory(),
            DevelopmentDirectory = developmentDirectory,
        }, [], loggerFactory).Load();
    }
}
