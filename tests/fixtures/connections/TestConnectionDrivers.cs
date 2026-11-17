using System.Text;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;

namespace Invoklet.Tests.Fixtures.Connections;

/// <summary>
/// An HTTP API connection for the application's own tests, with the fields an HTTP connector has (base address, headers, timeout) and an
/// HttpClient as its client. The connectors themselves are plugins, built and tested apart from the application.
/// </summary>
public sealed class HttpTestDriver : IConnectionDriver, ICSharpClientSource
{
    public string Id => "http";

    public string DisplayName => "HTTP";

    public ConnectionCategory Category => ConnectionCategory.Http;

    public IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>();

    public IReadOnlyList<ConnectionParam> Params { get; } =
    [
        new("baseAddress", "Base address", Required: true, Example: "https://api.example.com/"),
        new("headers", "Headers", ConnectionParamType.Map),
        new("timeout", "Timeout (seconds)", ConnectionParamType.Number, Default: "100", Advanced: true),
    ];

    public IReadOnlyList<string> Validate(MdRunConnection connection) =>
        connection.Params.TryGetValue("baseAddress", out var address) && !string.IsNullOrWhiteSpace(address.Text)
            ? []
            : ["The base address is missing."];

    public Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, "The server answered 200 OK."));

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context)
    {
        var parameters = connection.Params;
        var sb = new StringBuilder()
            .Append("public sealed class ").Append(context.ClassName).Append(" : global::System.Net.Http.HttpClient\n{\n")
            .Append("    internal ").Append(context.ClassName).Append("()\n    {\n")
            .Append("        BaseAddress = new global::System.Uri(").Append(context.Text(parameters["baseAddress"].Text!)).Append(");\n");
        if (parameters.TryGetValue("timeout", out var timeout) && timeout.Text is { Length: > 0 } seconds)
            sb.Append("        Timeout = global::System.TimeSpan.FromSeconds(global::System.Double.Parse(").Append(context.Text(seconds))
                .Append(", global::System.Globalization.CultureInfo.InvariantCulture));\n");
        foreach (var (name, value) in parameters.TryGetValue("headers", out var headers) ? headers.Fields ?? [] : [])
            sb.Append("        DefaultRequestHeaders.TryAddWithoutValidation(").Append(ConnectionCodeContext.Literal(name)).Append(", ")
                .Append(context.RuntimeText(value.Text ?? "")).Append(");\n");
        sb.Append("    }\n}");
        return new ConnectionClientCode(sb.ToString());
    }
}

/// <summary>A database connection edited as a connection string with a field per setting and a choice of how to sign in.</summary>
public sealed class DatabaseTestDriver : IConnectionDriver
{
    private static readonly ConnectionStringPart Passfile = new("Passfile", "pgpass file", Example: "~/.pgpass");

    public string Id => "postgres";

    public string DisplayName => "PostgreSQL";

    public ConnectionCategory Category => ConnectionCategory.Database;

    public IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>();

    public IReadOnlyList<ConnectionParam> Params { get; } =
    [
        new("connectionString", "Connection string", ConnectionParamType.ConnectionString, Required: true,
            Default: "Host=localhost;Port=5432;Database=postgres;Username=postgres",
            Parts:
            [
                new("Host", "Host", ["Server"], "localhost"),
                new("Port", "Port", Example: "5432"),
                new("Database", "Database", ["DB"], "postgres"),
                new("Username", "Username", ["User ID", "UID", "User"], "postgres"),
                new("Password", "Password", ["PWD"], "${secret:name}", IsSecret: true),
                Passfile,
            ],
            AuthModes:
            [
                new("Password", ["Host", "Port", "Database", "Username", "Password"]),
                new("pgpass file", ["Host", "Port", "Database", "Username", "Passfile"], Defaults: [new(Passfile, "~/.pgpass")]),
            ]),
        new("schema", "Schema"),
    ];

    public IReadOnlyList<string> Validate(MdRunConnection connection) => [];

    public Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, "Connected."));
}
