using System.Collections.Concurrent;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.NonUI.Tests.Connectors.EfCore.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Connectors.EfCore;

// The context written from EF Core's own model is compiled and used: queries, navigation properties, a write, a view and a composite key.
public sealed class EfContextScriptTests : IDisposable
{
    private readonly ShopDatabaseFixture _shop = new();

    public void Dispose() => _shop.Dispose();

    [Fact]
    [Trait("Category", "RealBuild")]
    public async Task AScript_Should_UseTheContextOfTheDatabase()
    {
        if (!new DotnetSdkProvider().Get().Any()) return;
        var driver = new SqliteEfDriver();
        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = driver.Id,
            Params = new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(_shop.ConnectionString) },
        };
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var schemas = new ConnectionSchemas(drivers, Path.Combine(_shop.Folder, "schemas"), DotnetConnectionTools.Default, _ => null);
        Assert.True(await schemas.EnsureAsync([connection], null, ConnectionSchemaRefresh.Missing, TestContext.Current.CancellationToken));
        Assert.Null(schemas.Cache.ErrorFor(connection));
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;
        Assert.Empty(generated.Errors);

        var outputDir = Path.Combine(_shop.Folder, "build");
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
        const string script = """
            using Microsoft.EntityFrameworkCore;
            var shop = Connections.Shop;
            Console.WriteLine(string.Join(",", shop.Customers.Where(c => c.City == "Oslo").Select(c => c.Name)));
            var ann = shop.Customers.Include(c => c.Orders).Single(c => c.Name == "Ann");
            Console.WriteLine(ann.Orders.Count + " " + ann.Orders.Sum(o => o.Total).ToString().Replace(',', '.'));
            Console.WriteLine(shop.Orders.Include(o => o.Customer).First(o => o.Total == 99).Customer.Name);
            Console.WriteLine(shop.OrderItems.Count(i => i.OrderId == 1 && i.Line == 2));
            Console.WriteLine(shop.BigOrders.Count());
            shop.Customers.Add(new Connections.ShopClient.Customer { Name = "Eve", City = "Bergen" });
            shop.SaveChanges();
            Console.WriteLine(shop.Customers.Count() + " " + (shop.Customers.Single(c => c.Name == "Eve").Created is not null));
            """;
        var result = await host.RunAsync(script, (data, kind) => (kind == OutputKind.StandardError ? errors : output).Enqueue(data),
                TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.Equal(["Ann", "2 170.5", "Bob", "1", "1", "3 True"], output.TakeLast(6));
    }
}
