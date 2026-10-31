using Invoklet.Connectors.EfCore;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Invoklet.NonUI.Tests.Connectors.EfCore.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Connectors.EfCore;

// Entity Framework Core's reverse engineering reads a database file here as it reads a server: the names, types, keys and navigation properties are its own.
public sealed class EfModelReaderTests : IDisposable
{
    private readonly ShopDatabaseFixture _shop = new();

    public void Dispose() => _shop.Dispose();

    private static Dictionary<string, MdRunValue> Resolved(string connectionString) =>
        new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(connectionString) };

    [Fact]
    public async Task FetchSchemaAsync_ADatabase_IsDescribedByTheModelOfEfCore()
    {
        // Arrange
        var sut = new SqliteEfDriver();

        // Act
        var schema = await sut.FetchSchemaAsync(Resolved(_shop.ConnectionString), new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("3 tables and 1 view: big_orders, customers, order_items, orders.", schema.Summary);
        var entities = System.Text.Json.JsonSerializer.Deserialize<List<EfEntity>>(schema.Data)!;
        var customers = entities.Single(e => e.Name == "customers");
        Assert.Equal(("Customer", "Customers"), (customers.ClassName, customers.SetName));
        Assert.Equal(["Id", "Name", "City", "Created"], customers.Columns.Select(c => c.Property).OrderBy(p => p == "Id" ? 0 : p == "Name" ? 1 : p == "City" ? 2 : 3));
        Assert.Equal(1, customers.Columns.Single(c => c.Property == "Id").PrimaryKey);
        Assert.True(customers.Columns.Single(c => c.Property == "Name").NotNull);
        Assert.False(customers.Columns.Single(c => c.Property == "City").NotNull);

        var orders = entities.Single(e => e.Name == "orders");
        var foreignKey = Assert.Single(orders.ForeignKeys);
        Assert.Equal(("Customer", "Orders", "customers"), (foreignKey.Reference, foreignKey.Collection, foreignKey.Table));

        Assert.Equal([1, 2], entities.Single(e => e.Name == "order_items").Columns.Where(c => c.PrimaryKey > 0).Select(c => c.PrimaryKey).Order());
        var view = entities.Single(e => e.Name == "big_orders");
        Assert.True(view.IsView);
        Assert.All(view.Columns, c => Assert.Equal(0, c.PrimaryKey));
    }

    [Fact]
    public async Task TestAsync_ADatabaseThatCannotBeOpened_SaysWhy()
    {
        var sut = new SqliteEfDriver();

        var result = await sut.TestAsync(Resolved($"Data Source={Path.Combine(_shop.Folder, "missing", "x.db")};Mode=ReadOnly"), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.StartsWith("The database can't be read:", result.Message);
    }

    [Fact]
    public async Task TestAsync_NoConnectionString_SaysSo()
    {
        var sut = new SqliteEfDriver();

        var result = await sut.TestAsync(new Dictionary<string, MdRunValue>(), TestContext.Current.CancellationToken);

        Assert.Equal(new ConnectionTestResult(false, "The connection string is missing."), result);
    }
}
