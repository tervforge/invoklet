using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.VB;
using Microsoft.Extensions.Logging;

namespace Invoklet.BuiltIns;

/// <summary>Shared composition for the languages and connection registry used by both applications.</summary>
public static class BuiltInCatalog
{
    public static void RegisterLanguages(LanguageHostRegistry registry, ILoggerFactory loggerFactory, IRuntimeReferenceLocator runtimeReferences)
    {
        registry.Register(new CSharpLanguageHostFactory(loggerFactory, runtimeReferences));
        registry.Register(new VbLanguageHostFactory(loggerFactory, runtimeReferences));
    }

    public static ConnectionDriverRegistry CreateConnectionDrivers() =>
        new(["http", "openapi", "grpc", "sqlite", "postgres", "sqlserver"]);
}
