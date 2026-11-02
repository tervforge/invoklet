using System.Text.Json;
using Invoklet.Connectors.EfCore;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.EfCore;

// A row another client changed since it was read must not be overwritten by a script: the columns EF Core marks as concurrency tokens
// (a SQL Server rowversion) have to reach the generated context, or its updates carry no condition on them.
public sealed class EfConcurrencyTokenTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-concurrency").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public byte[] Version { get; set; } = [];
        public string Note { get; set; } = "";
    }

    private sealed class PeopleContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Person>(e =>
        {
            e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.Name).IsConcurrencyToken();
        });
    }

    [Fact]
    public void From_APropertyThatIsAConcurrencyToken_IsMarkedInTheDescription()
    {
        // Arrange
        using var context = new PeopleContext();
        var model = ((IInfrastructure<IServiceProvider>)context).Instance.GetService(typeof(IDesignTimeModel)) is IDesignTimeModel design ? design.Model : throw new InvalidOperationException();

        // Act
        var columns = Assert.Single(EfModelReader.From(model)).Columns.ToDictionary(c => c.Property);

        // Assert
        Assert.True(columns["Version"].ConcurrencyToken);
        Assert.True(columns["Name"].ConcurrencyToken);
        Assert.False(columns["Note"].ConcurrencyToken);
        Assert.False(columns["Id"].ConcurrencyToken);
    }

    [Fact]
    public void TheGeneratedContext_AColumnThatIsAConcurrencyToken_SaysSo()
    {
        // Arrange
        var driver = new SqlServerConnectionDriver();
        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = driver.Id,
            Params = new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of("Server=localhost;Database=shop") },
        };
        List<EfEntity> entities =
        [
            new("dbo", "customers", false, "Customer", "Customers",
            [
                new("id", "Id", "int", "int", true, true, 1, "OnAdd"),
                new("version", "Version", "timestamp", "byte[]", false, true, 0, "OnAddOrUpdate", ColumnType: "rowversion", ConcurrencyToken: true),
                new("name", "Name", "nvarchar(100)", "string", false, true, 0),
            ]),
        ];
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var schemas = new ConnectionSchemas(drivers, _folder, new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") }, _ => null);
        schemas.Cache.Put(connection, new ConnectionSchema("x", [], JsonSerializer.Serialize(entities)));

        // Act
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;

        // Assert
        Assert.Empty(generated.Errors);
        var lines = generated.Source.Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("e.Property(x => x.Version).IsConcurrencyToken();", lines);
        Assert.DoesNotContain("e.Property(x => x.Name).IsConcurrencyToken();", lines);
    }

    [Fact]
    public void TheDescriptionOfADatabase_ThatHasNoConcurrencyTokenYet_IsReadAgain()
    {
        // The descriptions already kept were made without the mark: a new version of the format makes them be read again.
        Assert.Equal("2", new SqlServerConnectionDriver().SchemaVersion);
        Assert.Equal("2", new Invoklet.Connectors.Postgres.PostgresConnectionDriver().SchemaVersion);
        Assert.Equal("5", new Invoklet.Connectors.Sqlite.SqliteConnectionDriver().SchemaVersion);
    }
}
