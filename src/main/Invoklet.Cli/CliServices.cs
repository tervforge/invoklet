using Invoklet.BuiltIns;
using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.Plugins.Hosting;
using Invoklet.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.Cli;

/// <summary>
/// What the runner runs scripts with: the registered languages, which build their hosts (with the
/// Invoklet.Runtime library) from a run's request, the installed .NET SDKs, and the connection drivers a
/// script's <c>connections:</c> are built with, and what some of them read from their endpoint first
/// (a gRPC server's services), shared with the Invoklet window. <see cref="CreateDefault"/> is the only place that knows
/// the concrete language modules, plugin loader and .NET backend; <see cref="CliRunner"/> uses just these contracts.
/// </summary>
/// <param name="BuildsFolder">Where scripts are built, a folder kept for each; null builds each run in a temporary folder.</param>
public sealed record CliServices(ILanguageHostRegistry Registry, ISdkProvider Sdks, ConnectionDriverRegistry? ConnectionDrivers = null,
    ConnectionSchemas? ConnectionSchemas = null, string? BuildsFolder = null)
{
    /// <summary>The builds of scripts run from the command line, beside the window's own data; what <c>invoklet</c> itself builds in.</summary>
    public static string DefaultBuildsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet", "cli-builds");

    public PluginSession Plugins { get; init; } = PluginSession.Empty;

    internal bool PluginDevelopmentEnabled { get; init; }

    public static CliServices CreateDefault(string? developmentDirectory = null)
    {
        var runtimeReferences = new DotnetRuntimeReferenceLocator();
        // The window's Secret Store, read and written the same way: a script asks for one value or saves one (Util.SetSecret).
        // Its Path Store too, read only: a script asks for one path (Util.GetPath).
        var registry = new LanguageHostRegistry { SecretReader = Secrets.Read, SecretSaver = Secrets.Put, PathReader = Paths.Read };
        BuiltInCatalog.RegisterLanguages(registry, NullLoggerFactory.Instance, runtimeReferences);
        var connectionDrivers = BuiltInCatalog.CreateConnectionDrivers();
        var schemas = new ConnectionSchemas(connectionDrivers, ConnectionSchemaCache.DefaultFolder,
            DotnetConnectionTools.Default, Secret);
        return new CliServices(registry, new DotnetSdkProvider(), connectionDrivers, schemas)
        {
            Plugins = PluginStartup.Load(developmentDirectory, NullLoggerFactory.Instance, [new ConnectionPluginRoleHandler(connectionDrivers)]),
            PluginDevelopmentEnabled = developmentDirectory is not null,
        };
    }

    private static readonly FileSecretStore Secrets = new();

    private static readonly FilePathStore Paths = new();

    // The Secret Store's value for a connection's schema; a missing or unreadable one is null, which the schema reports as missing.
    private static string? Secret(string name) => Secrets.Find(name);
}
