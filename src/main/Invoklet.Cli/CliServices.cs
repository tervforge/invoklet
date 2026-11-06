using Invoklet.Connectors.Grpc;
using Invoklet.Connectors.Http;
using Invoklet.Connectors.Postgres;
using Invoklet.Connectors.Sqlite;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.Languages.VB;
using Invoklet.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.Cli;

/// <summary>
/// What the runner runs scripts with: the registered languages, which build their hosts (with the
/// Invoklet.Runtime library) from a run's request, the installed .NET SDKs, and the connection drivers a
/// script's <c>connections:</c> are built with, and what some of them read from their endpoint first
/// (a gRPC server's services), shared with the Invoklet window. <see cref="CreateDefault"/> is the only place that knows
/// the concrete language modules, connectors and .NET backend; <see cref="CliRunner"/> uses just these contracts.
/// </summary>
/// <param name="BuildsFolder">Where scripts are built, a folder kept for each; null builds each run in a temporary folder.</param>
public sealed record CliServices(ILanguageHostRegistry Registry, ISdkProvider Sdks, ConnectionDriverRegistry? ConnectionDrivers = null,
    ConnectionSchemas? ConnectionSchemas = null, string? BuildsFolder = null)
{
    /// <summary>The builds of scripts run from the command line, beside the window's own data; what <c>invoklet</c> itself builds in.</summary>
    public static string DefaultBuildsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Invoklet", "cli-builds");

    public static CliServices CreateDefault()
    {
        var runtimeReferences = new DotnetRuntimeReferenceLocator();
        // The window's Secret Store, read and written the same way: a script asks for one value or saves one (Util.SetSecret).
        var registry = new LanguageHostRegistry { SecretReader = Secrets.Read, SecretSaver = Secrets.Put };
        registry.Register(new CSharpLanguageHostFactory(NullLoggerFactory.Instance, runtimeReferences));
        registry.Register(new VbLanguageHostFactory(NullLoggerFactory.Instance, runtimeReferences));
        var connectionDrivers = new ConnectionDriverRegistry();
        connectionDrivers.Register(new HttpConnectionDriver());
        connectionDrivers.Register(new OpenApiConnectionDriver());
        connectionDrivers.Register(new GrpcConnectionDriver());
        connectionDrivers.Register(new SqliteConnectionDriver());
        connectionDrivers.Register(new PostgresConnectionDriver());
        connectionDrivers.Register(new SqlServerConnectionDriver());
        var schemas = new ConnectionSchemas(connectionDrivers, ConnectionSchemaCache.DefaultFolder,
            DotnetConnectionTools.Default, Secret);
        return new CliServices(registry, new DotnetSdkProvider(), connectionDrivers, schemas);
    }

    private static readonly FileSecretStore Secrets = new();

    // The Secret Store's value for a connection's schema; a missing or unreadable one is null, which the schema reports as missing.
    private static string? Secret(string name) => Secrets.Find(name);
}
