using Invoklet.Connectors.Postgres;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Invoklet.Windows.Models;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// A connection string parameter with fields for some of its settings: a plain one, and those of the PostgreSQL and SQL Server drivers.
internal sealed class ConnectionFieldViewModelConnectionStringFixture
{
    public ConnectionParam Plain { get; } = new("connectionString", "Connection string", ConnectionParamType.ConnectionString,
        Parts: [new("Host", "Host", ["Server"]), new("Port", "Port"), new("Password", "Password", ["PWD"], IsSecret: true)]);

    public ConnectionParam Postgres { get; } = new PostgresConnectionDriver().Params.Single(p => p.Type == ConnectionParamType.ConnectionString);

    public ConnectionParam SqlServer { get; } = new SqlServerConnectionDriver().Params.Single(p => p.Type == ConnectionParamType.ConnectionString);

    public IReadOnlyList<string> Secrets { get; } = ["shop", "other"];

    public ConnectionFieldViewModel CreateSut(ConnectionParam param, string? text = null) =>
        new(param, text is null ? null : MdRunValue.Of(text), Secrets);

    public static bool Shown(ConnectionFieldViewModel sut, string key) => sut.Parts.Single(p => p.Part.Key == key).IsVisible;
}
