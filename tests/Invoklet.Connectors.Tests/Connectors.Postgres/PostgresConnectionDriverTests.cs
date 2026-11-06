using Invoklet.Connectors.Postgres;
using Invoklet.Format.MdRun;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.Postgres;

[Trait("Platform", "Independent")]
public class PostgresConnectionDriverTests
{
    private static MdRunConnection Connection(string? connectionString) => new()
    {
        Name = "shop",
        Driver = "postgres",
        Params = connectionString is null ? new(StringComparer.Ordinal) : new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(connectionString) },
    };

    [Fact]
    public void Params_AreAConnectionStringAndAnOptionalSchema_AndTheFormSaysHowToSignInWithoutAPassword()
    {
        var sut = new PostgresConnectionDriver();

        Assert.Equal(["connectionString", "schema"], sut.Params.Select(p => p.Key));
        Assert.True(sut.Params[0].Required);
        Assert.False(sut.Params[1].Required);
        Assert.Equal(["Password", "pgpass file", "Kerberos (GSSAPI)"], sut.Params[0].AuthModes!.Select(m => m.Label));
    }

    [Fact]
    public void Validate_AConnectionString_IsCheckedAsPostgresOwn()
    {
        var sut = new PostgresConnectionDriver();

        Assert.Equal(["The connection string is missing."], sut.Validate(Connection(null)));
        Assert.Empty(sut.Validate(Connection("Host=localhost;Database=shop;Username=app;Password=${secret:pg}")));
        Assert.Empty(sut.Validate(Connection("Host=localhost;Database=shop;Username=app")));
        Assert.Single(sut.Validate(Connection("Host=localhost;Bogus=1")));
    }

    [Theory]
    [InlineData("net8.0", "8.0.11")]
    [InlineData("net9.0", "9.0.4")]
    [InlineData("net10.0", PostgresConnectionDriver.EntityFrameworkVersion)]
    [InlineData("latest", PostgresConnectionDriver.EntityFrameworkVersion)]
    [InlineData(null, PostgresConnectionDriver.EntityFrameworkVersion)]
    public void PackagesFor_TheTargetFramework_PicksTheProviderThatRunsThere(string? framework, string expected)
        => Assert.Equal(expected, new PostgresConnectionDriver().PackagesFor(framework)["Npgsql.EntityFrameworkCore.PostgreSQL"]);

    [Fact]
    public void Explain_NoPassword_SaysWhichPgpassLineIsLookedFor()
    {
        var failure = new Npgsql.PostgresException("password authentication failed", "FATAL", "FATAL", "28P01");

        var message = PostgresConnectionDriver.Explain("Host=db.local;Port=5433;Database=shop;Username=app", failure);

        Assert.Contains("The connection string has none, and no line of the pgpass file matches db.local:5433:shop:app", message);
        Assert.Contains("Passfile= or PGPASSFILE", message);
    }

    [Fact]
    public void Explain_ARefusedPassword_NamesTheUser()
    {
        var failure = new Npgsql.PostgresException("password authentication failed", "FATAL", "FATAL", "28P01");

        Assert.StartsWith("The server refused the password of user 'app'.", PostgresConnectionDriver.Explain("Host=db;Database=shop;Username=app;Password=x", failure));
    }

    [Fact]
    public void Explain_AnotherFailure_IsReportedAsItIs()
        => Assert.Equal("The database can't be read: boom", PostgresConnectionDriver.Explain("Host=db", new Npgsql.NpgsqlException("boom")));

    // Npgsql takes Passfile as it is written: a file named from the home folder is found under the home of whoever reads it.
    [Fact]
    public void ExpandPassfile_AFileUnderTheHomeFolder_IsNamedUnderThatHome()
    {
        // Arrange
        var home = Path.Combine("home", "me");

        // Act
        var expanded = PostgresConnectionDriver.ExpandPassfile("Host=db;Passfile=~/.pgpass", home);

        // Assert
        Assert.Equal(Path.Join(home, ".pgpass"), new Npgsql.NpgsqlConnectionStringBuilder(expanded).Passfile);
    }

    [Fact]
    public void ExpandPassfile_AFullPath_LeavesTheConnectionStringAlone()
    {
        // Act
        var expanded = PostgresConnectionDriver.ExpandPassfile("Host=db;Passfile=/etc/pg/pass", Path.Combine("home", "me"));

        // Assert
        Assert.Equal("Host=db;Passfile=/etc/pg/pass", expanded);
    }
}
