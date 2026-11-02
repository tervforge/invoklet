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

// A foreign key on a unique column is a one-to-one relationship: the row on the other side is one row, not a collection, and the
// generated context says which side holds the key.
public sealed class EfRelationshipTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-relationship").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private sealed class Person
    {
        public int Id { get; set; }
        public Passport? Passport { get; set; }
        public List<Phone> Phones { get; set; } = [];
    }

    private sealed class Passport
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public Person? Person { get; set; }
    }

    private sealed class Phone
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public Person? Person { get; set; }
    }

    private sealed class PeopleContext : DbContext
    {
        public DbSet<Person> People => Set<Person>();
        public DbSet<Passport> Passports => Set<Passport>();
        public DbSet<Phone> Phones => Set<Phone>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Passport>().HasOne(p => p.Person).WithOne(p => p.Passport).HasForeignKey<Passport>(p => p.PersonId);
            modelBuilder.Entity<Phone>().HasOne(p => p.Person).WithMany(p => p.Phones).HasForeignKey(p => p.PersonId);
        }
    }

    [Fact]
    public void From_AForeignKeyOnAUniqueColumn_IsMarkedUniqueAndAnOrdinaryOneIsNot()
    {
        // Arrange
        using var context = new PeopleContext();
        var model = ((IInfrastructure<IServiceProvider>)context).Instance.GetService(typeof(IDesignTimeModel)) is IDesignTimeModel design ? design.Model : throw new InvalidOperationException();

        // Act
        var entities = EfModelReader.From(model).ToDictionary(e => e.ClassName.Split('.', '+').Last());

        // Assert
        var passport = Assert.Single(entities["Passport"].ForeignKeys);
        Assert.True(passport.IsUnique);
        Assert.Equal(("Person", "Passport"), (passport.Reference, passport.Collection));
        Assert.False(Assert.Single(entities["Phone"].ForeignKeys).IsUnique);
    }

    private string Generate(params EfEntity[] entities)
    {
        var driver = new SqlServerConnectionDriver();
        var connection = new MdRunConnection
        {
            Name = "shop",
            Driver = driver.Id,
            Params = new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of("Server=localhost;Database=shop") },
        };
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var schemas = new ConnectionSchemas(drivers, _folder, new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") }, _ => null);
        schemas.Cache.Put(connection, new ConnectionSchema("x", [], JsonSerializer.Serialize(entities)));
        var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, drivers, schemas.Cache)!;
        Assert.Empty(generated.Errors);
        return generated.Source;
    }

    private static EfEntity[] PeopleWith(bool unique) =>
    [
        new("dbo", "person", false, "Person", "People", [new("id", "Id", "int", "int", true, true, 1, "OnAdd")]),
        new("dbo", "passport", false, "Passport", "Passports",
        [
            new("id", "Id", "int", "int", true, true, 1, "OnAdd"),
            new("person_id", "PersonId", "int", "int", true, true, 0),
        ])
        {
            ForeignKeys = [new EfForeignKey("dbo", "person", ["person_id"], ["id"], "Person", ["PersonId"], ["Id"], "Person", "Passport", unique)],
        },
    ];

    [Fact]
    public void TheGeneratedContext_AUniqueForeignKey_IsOneToOneWithASingleRowOnTheOtherSide()
    {
        // Act
        var source = Generate(PeopleWith(unique: true));

        // Assert
        var lines = source.Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("e.HasOne(x => x.Person).WithOne(x => x.Passport).HasForeignKey<Passport>(x => x.PersonId).HasPrincipalKey<Person>(x => x.Id);", lines);
        Assert.Contains("public Passport? Passport { get; set; }", lines);
        Assert.DoesNotContain(lines, l => l.Contains("ICollection<Passport>", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGeneratedContext_AnOrdinaryForeignKey_StaysOneToMany()
    {
        // Act
        var source = Generate(PeopleWith(unique: false));

        // Assert
        var lines = source.Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("e.HasOne(x => x.Person).WithMany(x => x.Passport).HasForeignKey(x => x.PersonId).HasPrincipalKey(x => x.Id);", lines);
        Assert.Contains(lines, l => l.StartsWith("public global::System.Collections.Generic.ICollection<Passport> Passport ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSchemaVersions_AreRaisedSoThatDescriptionsWithoutTheKeyAreReadAgain()
    {
        Assert.Equal("3", new SqlServerConnectionDriver().SchemaVersion);
        Assert.Equal("6", new Invoklet.Connectors.Sqlite.SqliteConnectionDriver().SchemaVersion);
    }
}
