using System.Collections.Concurrent;
using System.Text.Json;
using Invoklet.Connectors.Sql;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Connectors.Sql;

// The generated context is built against the real EF Core provider packages. There is no server in the test: the script only looks at the types.
public sealed class SqlContextCompileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-sql-compile").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static List<SqlTable> Tables() =>
    [
        new("public", "customers", false, [new("id", "int4", "int", true, true, 1, "OnAdd"), new("name", "text", "string", false, true, 0), new("meta", "jsonb", "string", false, false, 0, "None", "jsonb"), new("tags", "_text", "string[]", false, false, 0)]),
        new("public", "orders", false, [new("id", "int4", "int", true, true, 1), new("customer_id", "int4", "int", true, true, 0), new("placed", "timestamptz", "global::System.DateTimeOffset", true, true, 0)])
        {
            ForeignKeys = [new SqlForeignKey("public", "customers", ["customer_id"], ["id"])],
        },
        new("public", "order_view", true, [new("id", "int4", "int", true, true, 0)]),
    ];

    [Theory]
    [Trait("Category", "RealBuild")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public async Task TheGeneratedContext_Should_CompileAgainstTheProvider(string driverId)
    {
        if (!new DotnetSdkProvider().Get().Any()) return;
        SqlConnectionDriver driver = driverId == "postgres" ? new PostgresConnectionDriver() : new SqlServerConnectionDriver();
        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = driverId,
            Params = new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(driverId == "postgres" ? "Host=localhost;Database=shop" : "Server=localhost;Database=shop") },
        };
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var schemas = new ConnectionSchemas(drivers, Path.Combine(_folder, "schemas"), DotnetConnectionTools.Default, _ => null);
        schemas.Cache.Put(connection, new ConnectionSchema("x", [], JsonSerializer.Serialize(Tables())));
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;
        Assert.Empty(generated.Errors);

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
        // The model is built (keys, columns, the foreign key) without opening a connection.
        const string script = """
            using var shop = Connections.Shop;
            var customers = shop.Model.FindEntityType(typeof(Connections.ShopClient.Customer))!;
            Console.WriteLine(customers.FindPrimaryKey()!.Properties.Single().Name);
            var orders = shop.Model.FindEntityType(typeof(Connections.ShopClient.Order))!;
            Console.WriteLine(orders.GetForeignKeys().Single().PrincipalEntityType.ClrType.Name);
            Console.WriteLine(shop.Model.FindEntityType(typeof(Connections.ShopClient.OrderViewRow))!.FindPrimaryKey() is null);
            """;
        var result = await host.RunAsync(script, (data, kind) => (kind == OutputKind.StandardError ? errors : output).Enqueue(data),
                TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.Equal(["Id", "Customer", "True"], output.TakeLast(3));
    }
}
