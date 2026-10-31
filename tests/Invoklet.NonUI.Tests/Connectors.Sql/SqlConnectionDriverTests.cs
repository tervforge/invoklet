using System.Text.Json;
using Invoklet.Connectors.Sql;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Xunit;

namespace Invoklet.NonUI.Tests.Connectors.Sql;

// What can be said about a PostgreSQL or SQL Server connection without a server: its parameters, the types, the names and the code it generates.
public class SqlConnectionDriverTests
{
    private static MdRunConnection Connection(string driver, string? connectionString, string? schema = null)
    {
        var parameters = new Dictionary<string, MdRunValue>(StringComparer.Ordinal);
        if (connectionString is not null) parameters["connectionString"] = MdRunValue.Of(connectionString);
        if (schema is not null) parameters["schema"] = MdRunValue.Of(schema);
        return new MdRunConnection { Name = "shop", Driver = driver, Params = parameters };
    }

    [Fact]
    public void Params_AreAConnectionStringAndAnOptionalSchema_AndNothingElse()
    {
        Assert.Equal(["connectionString", "schema"], new PostgresConnectionDriver().Params.Select(p => p.Key));
        Assert.Equal(["connectionString", "schema"], new SqlServerConnectionDriver().Params.Select(p => p.Key));
        Assert.True(new PostgresConnectionDriver().Params[0].Required);
        Assert.False(new PostgresConnectionDriver().Params[1].Required);
    }

    [Fact]
    public void Validate_AConnectionString_IsCheckedAsThatServersOwn()
    {
        var postgres = new PostgresConnectionDriver();
        var sqlServer = new SqlServerConnectionDriver();

        Assert.Equal(["The connection string is missing."], postgres.Validate(Connection("postgres", null)));
        Assert.Empty(postgres.Validate(Connection("postgres", "Host=localhost;Database=shop;Username=app;Password=${secret:pg}")));
        Assert.Empty(sqlServer.Validate(Connection("sqlserver", "Server=localhost;Database=Shop;User Id=app;Password=${secret:sql}")));
        Assert.Single(postgres.Validate(Connection("postgres", "Host=localhost;Bogus=1")));
        Assert.Single(sqlServer.Validate(Connection("sqlserver", "Server=localhost;Bogus=1")));
    }

    [Theory]
    [InlineData("net8.0", "8.0.11", "8.0.33")]
    [InlineData("net9.0", "9.0.4", "9.0.22")]
    [InlineData("net10.0", PostgresConnectionDriver.EntityFrameworkVersion, SqlServerConnectionDriver.EntityFrameworkVersion)]
    [InlineData("latest", PostgresConnectionDriver.EntityFrameworkVersion, SqlServerConnectionDriver.EntityFrameworkVersion)]
    [InlineData(null, PostgresConnectionDriver.EntityFrameworkVersion, SqlServerConnectionDriver.EntityFrameworkVersion)]
    public void PackagesFor_TheTargetFramework_PicksTheEfCoreThatRunsThere(string? framework, string postgres, string sqlServer)
    {
        Assert.Equal(postgres, new PostgresConnectionDriver().PackagesFor(framework)["Npgsql.EntityFrameworkCore.PostgreSQL"]);
        Assert.Equal(sqlServer, new SqlServerConnectionDriver().PackagesFor(framework)["Microsoft.EntityFrameworkCore.SqlServer"]);
    }

    [Theory]
    [InlineData("int4", "int", true, null)]
    [InlineData("int8", "long", true, null)]
    [InlineData("bool", "bool", true, null)]
    [InlineData("numeric", "decimal", true, null)]
    [InlineData("uuid", "global::System.Guid", true, null)]
    [InlineData("timestamptz", "global::System.DateTimeOffset", true, null)]
    [InlineData("timestamp", "global::System.DateTime", true, null)]
    [InlineData("date", "global::System.DateOnly", true, null)]
    [InlineData("bytea", "byte[]", false, null)]
    [InlineData("jsonb", "string", false, "jsonb")]
    [InlineData("text", "string", false, null)]
    [InlineData("varchar", "string", false, null)]
    [InlineData("_int4", "int[]", false, null)]
    [InlineData("_text", "string[]", false, null)]
    [InlineData("inet", "string", false, null)]
    public void PostgresTypes_ATypeName_IsTheClrTypeTheProviderMapsIt(string udt, string clr, bool value, string? columnType)
        => Assert.Equal((clr, value, columnType), PostgresTypes.Map(udt));

    [Theory]
    [InlineData("int", "int", true)]
    [InlineData("bigint", "long", true)]
    [InlineData("bit", "bool", true)]
    [InlineData("nvarchar", "string", false)]
    [InlineData("decimal", "decimal", true)]
    [InlineData("uniqueidentifier", "global::System.Guid", true)]
    [InlineData("datetime2", "global::System.DateTime", true)]
    [InlineData("datetimeoffset", "global::System.DateTimeOffset", true)]
    [InlineData("varbinary", "byte[]", false)]
    [InlineData("rowversion", "byte[]", false)]
    [InlineData("geography", "string", false)]
    public void SqlServerTypes_ATypeName_IsTheClrTypeTheProviderMapsIt(string type, string clr, bool value)
        => Assert.Equal((clr, value), SqlServerTypes.Map(type));

    private static List<SqlTable> Shop() =>
    [
        new("public", "customers", false, [new("id", "int4", "int", true, true, 1, "OnAdd"), new("name", "text", "string", false, true, 0), new("meta", "jsonb", "string", false, false, 0, "None", "jsonb")]),
        new("public", "orders", false, [new("id", "int4", "int", true, true, 1), new("customer_id", "int4", "int", true, true, 0)])
        {
            ForeignKeys = [new SqlForeignKey("public", "customers", ["customer_id"], ["id"])],
        },
        new("sales", "orders", false, [new("id", "int4", "int", true, true, 1)]),
        new("public", "customer_view", true, [new("name", "text", "string", false, true, 0)]),
    ];

    [Fact]
    public void Build_ManySchemas_PrefixesTheTablesOutsideTheDefaultOne_AndLinksForeignKeys()
    {
        var model = SqlModel.Build(Shop(), "ShopClient", "public");

        Assert.Equal(["Customers", "Orders", "SalesOrders", "CustomerView"], model.Select(e => e.SetName));
        Assert.Equal(["Customer", "Order", "SalesOrder", "CustomerViewRow"], model.Select(e => e.ClassName));
        var relation = Assert.Single(model[1].Relations);
        Assert.Equal(("Customer", "Orders"), (relation.ReferenceName, relation.CollectionName));
        Assert.True(model[3].Keyless);
    }

    // The generated source of a connection whose description is the tables above.
    private static string GeneratedSource(SqlConnectionDriver driver, MdRunConnection connection)
    {
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var folder = Directory.CreateTempSubdirectory("Invoklet-sql-schemas").FullName;
        try
        {
            var schemas = new ConnectionSchemas(drivers, folder, new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") }, _ => null);
            schemas.Cache.Put(connection, new ConnectionSchema("x", [], JsonSerializer.Serialize(Shop())));
            var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;
            Assert.Empty(generated.Errors);
            return generated.Source;
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Generate_TheTables_BecomeAnEfContextOfTheProvider()
    {
        var driver = new PostgresConnectionDriver();
        var connection = Connection("postgres", "Host=db;Database=shop;Password=${secret:pg}");
        var code = GeneratedSource(driver, connection);

        Assert.Contains("public sealed class ShopClient : global::Microsoft.EntityFrameworkCore.DbContext", code);
        Assert.Contains("global::Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql(optionsBuilder, ConnectionString);", code);
        Assert.Contains("public global::Microsoft.EntityFrameworkCore.DbSet<Customer> Customers => Set<Customer>();", code);
        Assert.Contains("ToTable(e, \"orders\", \"sales\")", code);
        Assert.Contains("ToView(e, \"customer_view\", \"public\")", code);
        Assert.Contains("HasColumnType(e.Property(x => x.Meta), \"jsonb\")", code);
        Assert.Contains("e.Property(x => x.Id).ValueGeneratedOnAdd();", code);
        Assert.Contains("e.HasOne(x => x.Customer).WithMany(x => x.Orders).HasForeignKey(x => x.CustomerId).HasPrincipalKey(x => x.Id);", code);
        Assert.Contains("public string Name { get; set; } = null!;", code);
    }

    [Fact]
    public void Summary_TablesAndViews_AreCounted()
        => Assert.Equal("3 tables and 1 view: customers, orders, orders, customer_view.", SqlConnectionDriver.Summary(Shop()));
}
