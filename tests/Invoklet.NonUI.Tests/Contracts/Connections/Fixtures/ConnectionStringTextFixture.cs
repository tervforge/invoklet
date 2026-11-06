using Invoklet.Contracts.Connections;

namespace Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;

// The settings and the ways of signing in of a PostgreSQL connection string, as its driver offers them.
internal sealed class ConnectionStringTextFixture
{
    public ConnectionStringPart Host { get; } = new("Host", "Host", ["Server"]);
    public ConnectionStringPart Password { get; } = new("Password", "Password", ["PWD"], IsSecret: true);
    public ConnectionStringPart Port { get; } = new("Port", "Port");

    public IReadOnlyList<ConnectionStringPart> Parts { get; }
    public IReadOnlyList<ConnectionAuthMode> Modes { get; }

    public ConnectionStringTextFixture()
    {
        var user = new ConnectionStringPart("Username", "Username", ["User ID"]);
        var passfile = new ConnectionStringPart("Passfile", "pgpass file");
        var integrated = new ConnectionStringPart("Integrated Security", "Integrated Security");
        Parts = [Host, user, Password, passfile];
        Modes =
        [
            new ConnectionAuthMode("Password", ["Host", "Username", "Password"]),
            new ConnectionAuthMode("pgpass file", ["Host", "Username", "Passfile"]),
            new ConnectionAuthMode("Kerberos", ["Host", "Username"], [new(integrated, "true")]),
        ];
    }

    public ConnectionAuthMode Mode(string label) => Modes.Single(m => m.Label == label);
}
