using System.Collections.Concurrent;
using Invoklet.Connectors.Grpc;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Grpc.Fixtures;

/// <summary>
/// A script's gRPC connection to a real server, read once for a test class: the servers, the schema read from them, the code generated for
/// the connection, and one build of a script that calls the server (for what the editor finds after a build). A test that runs a script of
/// its own builds it in a folder of its own.
/// </summary>
public sealed class GrpcScriptFixture : IAsyncLifetime
{
    public const string CallingScript = """
        var headers = new Grpc.Core.Metadata { { "x-trace", "1" } };
        await Connections.Inventory.Stock.GetItemAsync(new Inventory.V1.ItemRequest { Sku = "A0" }, headers);
        var item = await Connections.Inventory.Stock.GetItemAsync(new Inventory.V1.ItemRequest { Sku = "A1" }, headers);
        Console.WriteLine($"{item.Sku} {item.Count} {item.Caller} {headers.Count}");
        """;

    private readonly GrpcServersFixture _servers = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-grpc-script").FullName;

    public IReadOnlyDictionary<string, string> Packages { get; } = new GrpcConnectionDriver().Packages;
    public GeneratedConnections Generated { get; private set; } = null!;
    public DotnetSdk? Sdk { get; } = new DotnetSdkProvider().Get().FirstOrDefault();

    /// <summary>The folder of the one build made for the class, and the contract assembly of the connection's classes in it.</summary>
    public string BuiltDirectory { get; private set; } = "";
    public string Contract { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _servers.InitializeAsync();
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(new GrpcConnectionDriver());
        var schemas = new ConnectionSchemas(drivers, Path.Combine(_folder, "schemas"), DotnetConnectionTools.Default,
            name => name == "inventory-token" ? "t0k" : null);
        var connection = _servers.Inventory();
        await schemas.EnsureAsync([connection], null, ConnectionSchemaRefresh.Failed, CancellationToken.None);
        Generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, Packages.Keys, drivers, schemas.Cache)!;
        if (Sdk is null) return;

        BuiltDirectory = NewOutputDirectory();
        await using var host = CreateHost(BuiltDirectory);
        await RunAsync(host, CallingScript);
        Contract = Directory.GetFiles(Path.Combine(BuiltDirectory, "obj", "connection-contracts"), "*.dll").Single();
    }

    public async ValueTask DisposeAsync()
    {
        await _servers.DisposeAsync();
        Directory.Delete(_folder, true);
    }

    public string NewOutputDirectory() => Directory.CreateDirectory(Path.Combine(_folder, "build-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A host as the Invoklet window gives it: the script asks for the connection's secret, and gets it from here.</summary>
    public CSharpLanguageHost CreateHost(string outputDirectory)
    {
        var options = CompiledDotnetLanguageHostOptions.From(new ExecutionRequest
        {
            Platform = Sdk!,
            OutputDirectory = outputDirectory,
            Packages = Packages,
            Connections = Generated,
        }, new CSharpReferenceService().GetExtensionReference());
        return new CSharpLanguageHost(options, NullLoggerFactory.Instance) { SecretReader = name => name == "inventory-token" ? "t0k" : null };
    }

    public static async Task<(RunResult Result, IReadOnlyList<string> Output, IReadOnlyList<string> Errors)> RunAsync(CSharpLanguageHost host, string script)
    {
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var result = await host.RunAsync(script, (data, kind) => (kind == OutputKind.StandardError ? errors : output).Enqueue(data), CancellationToken.None)
            .WaitAsync(TimeSpan.FromMinutes(3));
        return (result, [.. output], [.. errors]);
    }

    /// <summary>An editor that knows the connection, built against <paramref name="outputDirectory"/> when given (its contract, once built).</summary>
    public CSharpIntelliSenseService Editor(string? outputDirectory = null, bool withConnection = true)
    {
        var references = new CSharpReferenceService();
        var editor = new CSharpIntelliSenseService(references.GetDefault().Append(references.GetExtensionReference()),
            NullLogger<CSharpIntelliSenseService>.Instance);
        editor.UpdateExecutionContext(new ExecutionRequest
        {
            Platform = outputDirectory is null ? null : Sdk,
            OutputDirectory = outputDirectory,
            Packages = Packages,
            Connections = withConnection ? Generated : null,
        });
        return editor;
    }
}
