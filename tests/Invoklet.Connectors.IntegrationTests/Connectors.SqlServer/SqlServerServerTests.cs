using Invoklet.Connectors.EfCore;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests;

// The reverse engineering against a real SQL Server: the registration of the provider's design-time services, the version of the provider and what the
// server reports. Runs where INVOKLET_TEST_SQLSERVER names a server (the CI's Linux job starts one); skipped otherwise.
public sealed class SqlServerServerTests : IAsyncLifetime
{
    private const string Variable = "INVOKLET_TEST_SQLSERVER";
    private const string Schema = "invoklet_it";

    private static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    private static async Task RunAsync(string text, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(text, @"^GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline).Where(b => b.Trim().Length > 0))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async ValueTask InitializeAsync()
    {
        if (ConnectionString is not { Length: > 0 }) return;
        await RunAsync($"""
            IF OBJECT_ID('{Schema}.big_orders', 'V') IS NOT NULL DROP VIEW {Schema}.big_orders;
            IF OBJECT_ID('{Schema}.order_items', 'U') IS NOT NULL DROP TABLE {Schema}.order_items;
            IF OBJECT_ID('{Schema}.orders', 'U') IS NOT NULL DROP TABLE {Schema}.orders;
            IF OBJECT_ID('{Schema}.customers', 'U') IS NOT NULL DROP TABLE {Schema}.customers;
            IF TYPE_ID('{Schema}.OrderId') IS NOT NULL DROP TYPE {Schema}.OrderId;
            IF SCHEMA_ID('{Schema}') IS NOT NULL DROP SCHEMA {Schema};
            GO
            CREATE SCHEMA {Schema};
            GO
            CREATE TYPE {Schema}.OrderId FROM int NOT NULL;
            GO
            CREATE TABLE {Schema}.customers (
                id int IDENTITY(1, 1) PRIMARY KEY,
                name nvarchar(100) NOT NULL,
                total decimal(19, 4) NOT NULL DEFAULT 0,
                created datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
                doubled AS (total * 2),
                version rowversion);
            GO
            CREATE TABLE {Schema}.orders (
                id {Schema}.OrderId PRIMARY KEY,
                customer_id int NOT NULL REFERENCES {Schema}.customers (id));
            GO
            CREATE TABLE {Schema}.order_items (
                order_id {Schema}.OrderId NOT NULL REFERENCES {Schema}.orders (id),
                line int NOT NULL,
                product nvarchar(50) NOT NULL,
                PRIMARY KEY (order_id, line));
            GO
            CREATE VIEW {Schema}.big_orders AS SELECT id, customer_id FROM {Schema}.orders;
            """, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (ConnectionString is not { Length: > 0 }) return;
        await RunAsync($"""
            IF OBJECT_ID('{Schema}.big_orders', 'V') IS NOT NULL DROP VIEW {Schema}.big_orders;
            IF OBJECT_ID('{Schema}.order_items', 'U') IS NOT NULL DROP TABLE {Schema}.order_items;
            IF OBJECT_ID('{Schema}.orders', 'U') IS NOT NULL DROP TABLE {Schema}.orders;
            IF OBJECT_ID('{Schema}.customers', 'U') IS NOT NULL DROP TABLE {Schema}.customers;
            IF TYPE_ID('{Schema}.OrderId') IS NOT NULL DROP TYPE {Schema}.OrderId;
            IF SCHEMA_ID('{Schema}') IS NOT NULL DROP SCHEMA {Schema};
            """, CancellationToken.None);
    }

    private static Dictionary<string, MdRunValue> Resolved() => new(StringComparer.Ordinal)
    {
        ["connectionString"] = MdRunValue.Of(ConnectionString!),
        ["schema"] = MdRunValue.Of(Schema),
    };

    [Fact]
    public async Task FetchSchemaAsync_ARealServer_IsDescribedByTheProvidersReverseEngineering()
    {
        Assert.SkipUnless(ConnectionString is { Length: > 0 }, $"{Variable} names no server.");
        var sut = new SqlServerConnectionDriver();

        var schema = await sut.FetchSchemaAsync(Resolved(), new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") },
            TestContext.Current.CancellationToken);

        Assert.Equal("3 tables and 1 view: big_orders, customers, order_items, orders.", schema.Summary);
        var entities = System.Text.Json.JsonSerializer.Deserialize<List<EfEntity>>(schema.Data)!;
        var customers = entities.Single(e => e.Name == "customers");
        Assert.Equal(Schema, customers.Schema);
        var id = customers.Columns.Single(c => c.Name == "id");
        Assert.Equal((1, "OnAdd", "int"), (id.PrimaryKey, id.Generated, id.ClrType));
        Assert.Equal("decimal", customers.Columns.Single(c => c.Name == "total").ClrType);
        Assert.NotNull(customers.Columns.Single(c => c.Name == "doubled").ComputedSql);
        Assert.Equal("OnAddOrUpdate", customers.Columns.Single(c => c.Name == "version").Generated);
        Assert.Equal("byte[]", customers.Columns.Single(c => c.Name == "version").ClrType);
        // A rowversion guards updates: without the mark a script would overwrite a row another client has changed.
        Assert.True(customers.Columns.Single(c => c.Name == "version").ConcurrencyToken);

        // A column of an alias type is the type it is an alias of.
        var orders = entities.Single(e => e.Name == "orders");
        Assert.Equal("int", orders.Columns.Single(c => c.Name == "id").ClrType);
        var foreignKey = Assert.Single(orders.ForeignKeys);
        Assert.Equal(("Customer", "Orders"), (foreignKey.Reference, foreignKey.Collection));
        Assert.True(entities.Single(e => e.Name == "big_orders").IsView);
    }
}
