using Invoklet.Connectors.EfCore;
using Invoklet.Connectors.Postgres;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Npgsql;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests;

// The reverse engineering against a real PostgreSQL server: the registration of the provider's design-time services, the version of the provider
// and what the server reports. Runs where INVOKLET_TEST_POSTGRES names a server (the CI's Linux job starts one); skipped otherwise.
public sealed class PostgresServerTests : IAsyncLifetime
{
    private const string Variable = "INVOKLET_TEST_POSTGRES";
    private const string Schema = "invoklet_it";

    private static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    public async ValueTask InitializeAsync()
    {
        if (ConnectionString is not { Length: > 0 } connectionString) return;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DROP SCHEMA IF EXISTS {Schema} CASCADE;
            CREATE SCHEMA {Schema};
            CREATE TYPE {Schema}.mood AS ENUM ('sad', 'ok', 'happy');
            CREATE TABLE {Schema}.customers (
                id serial PRIMARY KEY,
                name text NOT NULL,
                meta jsonb,
                total numeric(19, 4) NOT NULL DEFAULT 0,
                created timestamptz NOT NULL DEFAULT now(),
                tags text[],
                feeling {Schema}.mood,
                address inet);
            CREATE TABLE {Schema}.orders (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                customer_id int NOT NULL REFERENCES {Schema}.customers (id),
                placed date NOT NULL DEFAULT current_date);
            CREATE TABLE {Schema}.order_items (
                order_id bigint NOT NULL REFERENCES {Schema}.orders (id),
                line int NOT NULL,
                product text NOT NULL,
                PRIMARY KEY (order_id, line));
            CREATE VIEW {Schema}.big_orders AS SELECT id, customer_id FROM {Schema}.orders;
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (ConnectionString is not { Length: > 0 } connectionString) return;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {Schema} CASCADE;";
        await command.ExecuteNonQueryAsync();
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
        var sut = new PostgresConnectionDriver();

        var schema = await sut.FetchSchemaAsync(Resolved(), new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") },
            TestContext.Current.CancellationToken);

        Assert.Equal("3 tables and 1 view: big_orders, customers, order_items, orders.", schema.Summary);
        var entities = System.Text.Json.JsonSerializer.Deserialize<List<EfEntity>>(schema.Data)!;
        var customers = entities.Single(e => e.Name == "customers");
        Assert.Equal(Schema, customers.Schema);
        Assert.Equal(("Customer", "Customers"), (customers.ClassName, customers.SetName));
        var id = customers.Columns.Single(c => c.Name == "id");
        Assert.Equal((1, "OnAdd", "int"), (id.PrimaryKey, id.Generated, id.ClrType));
        Assert.Equal("jsonb", customers.Columns.Single(c => c.Name == "meta").ColumnType);
        Assert.Equal("string[]", customers.Columns.Single(c => c.Name == "tags").ClrType);
        Assert.NotNull(customers.Columns.Single(c => c.Name == "created").DefaultSql);
        Assert.Equal("numeric(19,4)", customers.Columns.Single(c => c.Name == "total").ColumnType?.Replace(" ", ""));

        var orders = entities.Single(e => e.Name == "orders");
        Assert.Equal("OnAdd", orders.Columns.Single(c => c.Name == "id").Generated);
        var foreignKey = Assert.Single(orders.ForeignKeys);
        Assert.Equal(("Customer", "Orders"), (foreignKey.Reference, foreignKey.Collection));
        Assert.Equal([1, 2], entities.Single(e => e.Name == "order_items").Columns.Where(c => c.PrimaryKey > 0).Select(c => c.PrimaryKey).Order());
        Assert.True(entities.Single(e => e.Name == "big_orders").IsView);
    }

    [Fact]
    public async Task TestAsync_AWrongPassword_SaysWhoWasRefused()
    {
        Assert.SkipUnless(ConnectionString is { Length: > 0 }, $"{Variable} names no server.");
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString!) { Password = "definitely-wrong", Pooling = false };
        var sut = new PostgresConnectionDriver();

        var result = await sut.TestAsync(new Dictionary<string, MdRunValue> { ["connectionString"] = MdRunValue.Of(builder.ToString()) },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains($"refused the password of user '{builder.Username}'", result.Message);
    }
}
