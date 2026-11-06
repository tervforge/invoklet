using System.Collections.Concurrent;
using System.Text.Json;
using Invoklet.Connectors.EfCore;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.Connectors.IntegrationTests.Connectors.EfCore.Fixtures;

// Compiles the context of a provider's connection against that provider's real EF Core package, with no server: the script only looks at the model.
internal sealed class ProviderCompileFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-provider-compile").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    // What a server would have given: a table with an identity key, a json column and a version column, a table that points to it, and a view.
    public static List<EfEntity> Shop() =>
    [
        new("public", "customers", false, "Customer", "Customers",
        [
            new("id", "Id", "int4", "int", true, true, 1, "OnAdd"),
            new("name", "Name", "text", "string", false, true, 0),
            new("meta", "Meta", "jsonb", "string", false, false, 0, ColumnType: "jsonb"),
            new("total", "Total", "numeric(19,4)", "decimal", true, false, 0, ColumnType: "numeric(19,4)", DefaultSql: "0"),
        ]),
        new("public", "orders", false, "Order", "Orders",
        [
            new("id", "Id", "int4", "int", true, true, 1),
            new("customer_id", "CustomerId", "int4", "int", true, true, 0),
        ])
        {
            ForeignKeys = [new EfForeignKey("public", "customers", ["customer_id"], ["id"], "Customer", ["CustomerId"], ["Id"], "Customer", "Orders")],
        },
        new("public", "order_view", true, "OrderView", "OrderViews", [new("id", "Id", "int4", "int", true, true, 0)]),
    ];

    public async Task<IReadOnlyList<string>> RunAsync(EfCoreConnectionDriver driver, string connectionString, string script)
    {
        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = driver.Id,
            Params = new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(connectionString) },
        };
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var schemas = new ConnectionSchemas(drivers, Path.Combine(_folder, "schemas"), DotnetConnectionTools.Default, _ => null);
        schemas.Cache.Put(connection, driver.SchemaOf(Shop()));
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;
        if (generated.Errors.Count > 0) throw new InvalidOperationException(string.Join('\n', generated.Errors));

        var outputDir = Path.Combine(_folder, "build");
        Directory.CreateDirectory(outputDir);
        var options = CompiledDotnetLanguageHostOptions.From(new ExecutionRequest
        {
            Platform = new DotnetSdkProvider().Get().First(),
            OutputDirectory = outputDir,
            WorkingDirectory = outputDir,
            Packages = driver.Packages,
            Connections = generated,
        }, new CSharpReferenceService().GetExtensionReference());
        await using var host = new CSharpLanguageHost(options, NullLoggerFactory.Instance);
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var result = await host.RunAsync(script, (data, kind) => (kind == OutputKind.StandardError ? errors : output).Enqueue(data), CancellationToken.None)
            .WaitAsync(TimeSpan.FromMinutes(5));
        if (!result.Succeeded) throw new InvalidOperationException(string.Join('\n', errors));
        return [.. output];
    }

    // The model is built (keys, columns, the foreign key) without opening a connection.
    public const string ModelScript = """
        using var shop = Connections.Shop;
        var customers = shop.Model.FindEntityType(typeof(Connections.ShopClient.Customer))!;
        Console.WriteLine(customers.FindPrimaryKey()!.Properties.Single().Name);
        var orders = shop.Model.FindEntityType(typeof(Connections.ShopClient.Order))!;
        Console.WriteLine(orders.GetForeignKeys().Single().PrincipalEntityType.ClrType.Name);
        Console.WriteLine(shop.Model.FindEntityType(typeof(Connections.ShopClient.OrderView))!.FindPrimaryKey() is null);
        """;
}
