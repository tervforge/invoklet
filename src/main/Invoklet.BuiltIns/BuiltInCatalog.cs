using Invoklet.Connectors.Grpc;
using Invoklet.Connectors.Http;
using Invoklet.Connectors.Postgres;
using Invoklet.Connectors.Sqlite;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.VB;
using Microsoft.Extensions.Logging;

namespace Invoklet.BuiltIns;

/// <summary>The modules shipped in both applications. Each host owns its registries and script access policies.</summary>
public static class BuiltInCatalog
{
    public static void RegisterLanguages(LanguageHostRegistry registry, ILoggerFactory loggerFactory, IRuntimeReferenceLocator runtimeReferences)
    {
        registry.Register(new CSharpLanguageHostFactory(loggerFactory, runtimeReferences));
        registry.Register(new VbLanguageHostFactory(loggerFactory, runtimeReferences));
    }

    public static ConnectionDriverRegistry CreateConnectionDrivers()
    {
        var registry = new ConnectionDriverRegistry();
        registry.Register(new HttpConnectionDriver());
        registry.Register(new OpenApiConnectionDriver());
        registry.Register(new GrpcConnectionDriver());
        registry.Register(new SqliteConnectionDriver());
        registry.Register(new PostgresConnectionDriver());
        registry.Register(new SqlServerConnectionDriver());
        return registry;
    }
}
