using Invoklet.Connectors.SqlServer;
using Invoklet.Format.MdRun;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.SqlServer;

public class SqlServerConnectionDriverTests
{
    private static MdRunConnection Connection(string? connectionString) => new()
    {
        Name = "shop",
        Driver = "sqlserver",
        Params = connectionString is null ? new(StringComparer.Ordinal) : new(StringComparer.Ordinal) { ["connectionString"] = MdRunValue.Of(connectionString) },
    };

    [Fact]
    public void Params_AreAConnectionStringAndAnOptionalSchema_AndTheFormMentionsIntegratedSecurity()
    {
        var sut = new SqlServerConnectionDriver();

        Assert.Equal(["connectionString", "schema"], sut.Params.Select(p => p.Key));
        Assert.Contains("Integrated Security=true", sut.Params[0].Description);
    }

    [Fact]
    public void Validate_AConnectionString_IsCheckedAsSqlServerOwn()
    {
        var sut = new SqlServerConnectionDriver();

        Assert.Equal(["The connection string is missing."], sut.Validate(Connection(null)));
        Assert.Empty(sut.Validate(Connection("Server=localhost;Database=Shop;User Id=app;Password=${secret:sql}")));
        Assert.Empty(sut.Validate(Connection("Server=localhost;Database=Shop;Integrated Security=true")));
        Assert.Single(sut.Validate(Connection("Server=localhost;Bogus=1")));
    }

    [Theory]
    [InlineData("net8.0", "8.0.33")]
    [InlineData("net9.0", "9.0.22")]
    [InlineData("net10.0", SqlServerConnectionDriver.EntityFrameworkVersion)]
    [InlineData("latest", SqlServerConnectionDriver.EntityFrameworkVersion)]
    [InlineData(null, SqlServerConnectionDriver.EntityFrameworkVersion)]
    public void PackagesFor_TheTargetFramework_PicksTheProviderThatRunsThere(string? framework, string expected)
        => Assert.Equal(expected, new SqlServerConnectionDriver().PackagesFor(framework)["Microsoft.EntityFrameworkCore.SqlServer"]);

    [Fact]
    public void Explain_IntegratedSecurityOffWindows_MentionsKerberos()
    {
        var message = SqlServerConnectionDriver.Explain("Server=db;Database=Shop;Integrated Security=true", "Cannot generate SSPI context.");

        if (OperatingSystem.IsWindows()) Assert.Equal("The database can't be read: Cannot generate SSPI context.", message);
        else Assert.Contains("kinit", message);
        Assert.Equal("The database can't be read: x", SqlServerConnectionDriver.Explain("Server=db;User Id=a;Password=b", "x"));
    }

}
