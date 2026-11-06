using Invoklet.Connectors.Postgres;
using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Windows.Models;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// A connection string is edited through fields for its settings, the rest of the text staying as written, and a database's form offers
// its ways of signing in.
public sealed class ConnectionFieldViewModelConnectionStringTests
{
    [Fact]
    public void Constructor_AStoredConnectionString_ShowsItsSettingsInTheFields()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Plain, "Server=db.local;SSL Mode=Require;Password=${secret:shop}");

        // Assert
        Assert.Equal(["db.local", "", "${secret:shop}"], sut.Parts.Select(p => p.Value));
    }

    [Fact]
    public void PartValue_FieldsEdited_RewriteOnlyTheirSettings()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();
        var sut = fixture.CreateSut(fixture.Plain, "Server=db.local;SSL Mode=Require;Password=${secret:shop}");

        // Act
        sut.Parts[1].Value = "5433";
        sut.Parts[0].Value = "db2.local";

        // Assert
        Assert.Equal("Server=db2.local;SSL Mode=Require;Password=${secret:shop};Port=5433", sut.ToValue()!.Text);
    }

    [Fact]
    public void Text_TheWholeTextEdited_UpdatesTheFields()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();
        var sut = fixture.CreateSut(fixture.Plain);

        // Act
        sut.Text = "Host=a;Port=1";

        // Assert
        Assert.Equal(["a", "1", ""], sut.Parts.Select(p => p.Value));
    }

    [Fact]
    public void Parts_ASecretField_IsTheOnlyOneOfferingTheSecrets()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Plain, "Host=a");

        // Assert
        Assert.Equal([false, false, true], sut.Parts.Select(p => p.CanInsertSecret));
    }

    [Fact]
    public void SecretToInsert_ASecretChosen_WritesItsPlaceholderIntoTheText()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();
        var sut = fixture.CreateSut(fixture.Plain, "Host=a");

        // Act
        sut.Parts[2].SecretToInsert = "shop";

        // Assert
        Assert.Equal("Host=a;Password=${secret:shop}", sut.Text);
    }

    [Fact]
    public void Constructor_APostgresStringWithAPassword_SignsInWithThePasswordAndHidesThePgpassFile()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Assert
        Assert.Equal("Password", sut.SelectedAuthMode!.Label);
        Assert.True(ConnectionFieldViewModelConnectionStringFixture.Shown(sut, "Password"));
        Assert.False(ConnectionFieldViewModelConnectionStringFixture.Shown(sut, "Passfile"));
    }

    // The usual pgpass file of this system goes into its empty field, and the password goes.
    [Fact]
    public void SelectedAuthMode_PgpassChosen_WritesTheSystemsFileAndShowsItsField()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Act
        sut.SelectedAuthMode = sut.AuthModes.Single(m => m.Label == "pgpass file");

        // Assert
        Assert.Equal("Host=db;Username=app;Passfile=" + PostgresConnectionDriver.DefaultPassfile, sut.Text);
        Assert.False(ConnectionFieldViewModelConnectionStringFixture.Shown(sut, "Password"));
        Assert.True(ConnectionFieldViewModelConnectionStringFixture.Shown(sut, "Passfile"));
        Assert.True(sut.HasAuthHint);
    }

    [Fact]
    public void Text_IntegratedSecurityWritten_SignsInWithKerberos()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Act
        sut.Text = "Host=db;Integrated Security=true";

        // Assert
        Assert.Equal("Kerberos (GSSAPI)", sut.SelectedAuthMode!.Label);
        Assert.False(ConnectionFieldViewModelConnectionStringFixture.Shown(sut, "Password"));
    }

    // A new connection starts with the server's own defaults written in, not with examples to copy.
    [Fact]
    public void Constructor_ANewPostgresConnection_StartsWithTheServersDefaults()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Postgres);

        // Assert
        Assert.Equal("Host=localhost;Port=5432;Database=postgres;Username=postgres", sut.Text);
        Assert.Equal(["localhost", "5432", "postgres", "postgres", "", ""], sut.Parts.Select(p => p.Value));
        Assert.Equal("Password", sut.SelectedAuthMode!.Label);
    }

    [Fact]
    public void Constructor_ANewSqlServerConnection_StartsWithTheServersDefaults()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(fixture.SqlServer);

        // Assert
        Assert.Equal("Server=localhost;Database=master;User Id=sa;TrustServerCertificate=true", sut.Text);
    }

    // Only a database's connection string starts filled in; another parameter's default stays a default, not text.
    [Fact]
    public void Constructor_ANewParameterWithADefault_StartsEmpty()
    {
        // Arrange
        var fixture = new ConnectionFieldViewModelConnectionStringFixture();

        // Act
        var sut = fixture.CreateSut(new ConnectionParam("timeout", "Timeout", ConnectionParamType.Number, Default: "100"));

        // Assert
        Assert.Equal("", sut.Text);
    }
}
