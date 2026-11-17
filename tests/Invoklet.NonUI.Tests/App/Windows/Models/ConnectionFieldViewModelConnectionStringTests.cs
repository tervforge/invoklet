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
