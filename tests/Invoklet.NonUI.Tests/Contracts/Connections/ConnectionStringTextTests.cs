using Invoklet.Contracts.Connections;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// The connection form edits one setting of a connection string and leaves the rest of the text as the user wrote it.
[Trait("Platform", "Independent")]
public sealed class ConnectionStringTextTests
{
    private static readonly ConnectionStringPart Host = new("Host", "Host", ["Server"]);
    private static readonly ConnectionStringPart Password = new("Password", "Password", ["PWD"], IsSecret: true);
    private static readonly ConnectionStringPart Port = new("Port", "Port");

    [Theory]
    [InlineData("Host=db.local;Port=5432", "db.local")]
    [InlineData("server = db.local ; Port=5432", "db.local")]
    [InlineData("Port=5432", null)]
    [InlineData("", null)]
    [InlineData("Host=\"a;b\";Port=1", "a;b")]
    [InlineData("Host='it''s';Port=1", "it's")]
    public void Read_Should_FindTheSetting_ByItsKeyOrASynonym(string text, string? expected)
        => Assert.Equal(expected, ConnectionStringText.Read(text, Host));

    [Theory]
    // Only the value changes; the keys keep their case, the order and the other settings stay.
    [InlineData("HOST=old;SSL Mode=Require", "new", "HOST=new;SSL Mode=Require")]
    [InlineData("Server=old;Port=1", "new", "Server=new;Port=1")]
    // A setting the text lacks goes at the end, under the part's own key.
    [InlineData("Port=1", "db", "Port=1;Host=db")]
    [InlineData("Port=1;", "db", "Port=1;Host=db")]
    [InlineData("", "db", "Host=db")]
    // Empty takes it out.
    [InlineData("Port=1;Host=old;SSL Mode=Require", "", "Port=1;SSL Mode=Require")]
    [InlineData("Port=1", "", "Port=1")]
    // A value with a semicolon is quoted, and reads back the same.
    [InlineData("Port=1", "a;b", "Port=1;Host=\"a;b\"")]
    public void Write_Should_ChangeOnlyThatSetting(string text, string value, string expected)
        => Assert.Equal(expected, ConnectionStringText.Write(text, Host, value));

    [Fact]
    public void APlaceholder_Should_BeWrittenAndReadAsItIs()
    {
        var text = ConnectionStringText.Write("Host=db;Username=app", Password, "${secret:shop-db}");

        Assert.Equal("Host=db;Username=app;Password=${secret:shop-db}", text);
        Assert.Equal("${secret:shop-db}", ConnectionStringText.Read(text, Password));
    }

    // A quoted value of another setting may hold what looks like a key: it stays one value.
    [Fact]
    public void AQuotedValueOfAnotherSetting_Should_StayWhole()
    {
        var text = "Options=\"Port=9;x\";Host=db";

        Assert.Null(ConnectionStringText.Read(text, Port));
        Assert.Equal("Options=\"Port=9;x\";Host=db;Port=5432", ConnectionStringText.Write(text, Port, "5432"));
    }

    // The ways of signing in, as PostgreSQL offers them.
    private static readonly ConnectionStringPart User = new("Username", "Username", ["User ID"]);
    private static readonly ConnectionStringPart Passfile = new("Passfile", "pgpass file");
    private static readonly ConnectionStringPart Integrated = new("Integrated Security", "Integrated Security");
    private static readonly ConnectionStringPart[] Parts = [Host, User, Password, Passfile];
    private static readonly ConnectionAuthMode WithPassword = new("Password", ["Host", "Username", "Password"]);
    private static readonly ConnectionAuthMode WithPgpass = new("pgpass file", ["Host", "Username", "Passfile"]);
    private static readonly ConnectionAuthMode WithKerberos = new("Kerberos", ["Host", "Username"], [new(Integrated, "true")]);
    private static readonly ConnectionAuthMode[] Modes = [WithPassword, WithPgpass, WithKerberos];

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
    public void Mode_Should_BeReadBackFromTheText(string text, string expected)
        => Assert.Equal(expected, ConnectionStringText.Mode(text, Modes, Parts)!.Label);

    // Choosing a way takes out the fields and settings of the others and writes its own; everything else stays.
    [Theory]
    [InlineData("Host=a;Username=u;Password=x;SSL Mode=Require", "pgpass file", "Host=a;Username=u;SSL Mode=Require")]
    [InlineData("Host=a;Username=u;Password=x", "Kerberos", "Host=a;Username=u;Integrated Security=true")]
    [InlineData("Host=a;Integrated Security=true;Passfile=/p", "Password", "Host=a")]
    public void WithMode_Should_WriteThatWayOfSigningIn(string text, string mode, string expected)
        => Assert.Equal(expected, ConnectionStringText.WithMode(text, Modes.Single(m => m.Label == mode), Modes, Parts));
}
