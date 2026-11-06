using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// The connection form edits one setting of a connection string and leaves the rest of the text as the user wrote it.
[Trait("Platform", "Independent")]
public sealed class ConnectionStringTextTests
{
    [Theory]
    [InlineData("Host=db.local;Port=5432", "db.local")]
    [InlineData("server = db.local ; Port=5432", "db.local")]
    [InlineData("Port=5432", null)]
    [InlineData("", null)]
    [InlineData("Host=\"a;b\";Port=1", "a;b")]
    [InlineData("Host='it''s';Port=1", "it's")]
    public void Read_ASettingByItsKeyOrASynonym_ReturnsItsValue(string text, string? expected)
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var value = ConnectionStringText.Read(text, fixture.Host);

        // Assert
        Assert.Equal(expected, value);
    }

    // Only the value changes; the keys keep their case, the order and the other settings stay. A setting the text lacks goes at the end
    // under the part's own key, empty takes it out, and a value with a semicolon is quoted.
    [Theory]
    [InlineData("HOST=old;SSL Mode=Require", "new", "HOST=new;SSL Mode=Require")]
    [InlineData("Server=old;Port=1", "new", "Server=new;Port=1")]
    [InlineData("Port=1", "db", "Port=1;Host=db")]
    [InlineData("Port=1;", "db", "Port=1;Host=db")]
    [InlineData("", "db", "Host=db")]
    [InlineData("Port=1;Host=old;SSL Mode=Require", "", "Port=1;SSL Mode=Require")]
    [InlineData("Port=1", "", "Port=1")]
    [InlineData("Port=1", "a;b", "Port=1;Host=\"a;b\"")]
    public void Write_AValue_ChangesOnlyThatSetting(string text, string value, string expected)
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var written = ConnectionStringText.Write(text, fixture.Host, value);

        // Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Write_APlaceholder_WritesItAsItIs()
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var written = ConnectionStringText.Write("Host=db;Username=app", fixture.Password, "${secret:shop-db}");

        // Assert
        Assert.Equal("Host=db;Username=app;Password=${secret:shop-db}", written);
    }

    [Fact]
    public void Read_APlaceholder_ReturnsItAsItIs()
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var value = ConnectionStringText.Read("Host=db;Password=${secret:shop-db}", fixture.Password);

        // Assert
        Assert.Equal("${secret:shop-db}", value);
    }

    // A quoted value of another setting may hold what looks like a key: it stays one value.
    [Fact]
    public void Read_AKeyInsideAnotherSettingsQuotedValue_FindsNothing()
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var value = ConnectionStringText.Read("Options=\"Port=9;x\";Host=db", fixture.Port);

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void Write_ASettingBesideAQuotedValue_KeepsTheQuotedValueWhole()
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var written = ConnectionStringText.Write("Options=\"Port=9;x\";Host=db", fixture.Port, "5432");

        // Assert
        Assert.Equal("Options=\"Port=9;x\";Host=db;Port=5432", written);
    }

    // The way is read back from the text, as nothing else is kept: a switched-on setting names its way, a pgpass file named in the text
    // its way; anything else (a password, or none, which the server looks up in pgpass anyway) the first way.
    [Theory]
    [InlineData("", "Password")]
    [InlineData("Host=a;Username=u;Password=${secret:x}", "Password")]
    [InlineData("Host=a;Username=u", "Password")]
    [InlineData("Host=a;Passfile=/home/u/.pgpass", "pgpass file")]
    [InlineData("Host=a;Integrated Security=true", "Kerberos")]
    [InlineData("Host=a;Integrated Security=SSPI", "Kerberos")]
    [InlineData("Host=a;Integrated Security=false;Password=x", "Password")]
    public void Mode_AConnectionString_ReturnsItsWayOfSigningIn(string text, string expected)
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var mode = ConnectionStringText.Mode(text, fixture.Modes, fixture.Parts);

        // Assert
        Assert.Equal(expected, mode!.Label);
    }

    // Choosing a way takes out the fields and settings of the others and writes its own; everything else stays.
    [Theory]
    [InlineData("Host=a;Username=u;Password=x;SSL Mode=Require", "pgpass file", "Host=a;Username=u;SSL Mode=Require")]
    [InlineData("Host=a;Username=u;Password=x", "Kerberos", "Host=a;Username=u;Integrated Security=true")]
    [InlineData("Host=a;Integrated Security=true;Passfile=/p", "Password", "Host=a")]
    public void WithMode_AWayOfSigningIn_WritesItsSettingsAndDropsTheOthers(string text, string mode, string expected)
    {
        // Arrange
        var fixture = new ConnectionStringTextFixture();

        // Act
        var written = ConnectionStringText.WithMode(text, fixture.Mode(mode), fixture.Modes, fixture.Parts);

        // Assert
        Assert.Equal(expected, written);
    }
}
