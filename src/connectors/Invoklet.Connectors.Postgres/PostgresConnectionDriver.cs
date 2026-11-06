using Invoklet.Connectors.EfCore;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Invoklet.Connectors.Postgres;

/// <summary>A PostgreSQL database, read with the Npgsql provider's reverse engineering and used by the script through the same provider.</summary>
public sealed class PostgresConnectionDriver : EfCoreConnectionDriver
{
    public const string DriverId = "postgres";

    // The version the script's context is built with.
    public const string EntityFrameworkVersion = "10.0.3";

    private const string Package = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public override string Id => DriverId;

    public override string DisplayName => "PostgreSQL";

    protected override string Kind => "PostgreSQL";

    protected override string UseCall => "global::Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql";

    protected override string DefaultConnectionString => "Host=localhost;Port=5432;Database=postgres;Username=postgres";

    // Where libpq and Npgsql look for the password file when none is named, under the user's home ("~", which Invoklet expands where
    // the file is read: Npgsql takes Passfile as it is written).
    internal static string DefaultPassfile => OperatingSystem.IsWindows() ? @"~\AppData\Roaming\postgresql\pgpass.conf" : "~/.pgpass";

    private static readonly ConnectionStringPart Passfile = new("Passfile", "pgpass file", Example: DefaultPassfile);

    // Npgsql's keys and the synonyms it takes for them.
    protected override IReadOnlyList<ConnectionStringPart> ConnectionStringParts { get; } =
    [
        new("Host", "Host", ["Server"], "localhost"),
        new("Port", "Port", Example: "5432"),
        new("Database", "Database", ["DB"], "postgres"),
        new("Username", "Username", ["User ID", "UserID", "User Name", "UID", "User"], "postgres"),
        new("Password", "Password", ["PWD", "PSW"], "${secret:name}", IsSecret: true),
        Passfile,
    ];

    private static readonly ConnectionStringPart IntegratedSecurity = new("Integrated Security", "Integrated Security");

    protected override IReadOnlyList<ConnectionAuthMode> AuthModes { get; } =
    [
        new("Password", ["Host", "Port", "Database", "Username", "Password"]),
        new("pgpass file", ["Host", "Port", "Database", "Username", "Passfile"],
            Hint: "~ is your home folder.", Defaults: [new(Passfile, DefaultPassfile)]),
        new("Kerberos (GSSAPI)", ["Host", "Port", "Database", "Username"], [new(IntegratedSecurity, "true")],
            Hint: "Your Kerberos ticket (kinit)."),
    ];

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string> { [Package] = EntityFrameworkVersion };

    // The provider's release for the EF Core of the script's framework.
    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => MajorVersion(targetFramework) switch
    {
        8 => new Dictionary<string, string> { [Package] = "8.0.11" },
        9 => new Dictionary<string, string> { [Package] = "9.0.4" },
        _ => Packages,
    };

    protected override string? CheckConnectionString(string text)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(text);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return $"The connection string is not a PostgreSQL one: {ex.Message}";
        }
    }

    /// <summary>The connection string with a password file named from the home folder ("~/.pgpass") under <paramref name="home"/>.</summary>
    internal static string ExpandPassfile(string connectionString, string home)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Passfile is not { } passfile || !(passfile == "~" || passfile.StartsWith("~/", StringComparison.Ordinal) || passfile.StartsWith("~\\", StringComparison.Ordinal)))
            return connectionString;
        builder.Passfile = Path.Join(home, passfile[1..].TrimStart('/', '\\'));
        return builder.ConnectionString;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    protected override (string ConnectionString, string? Schema) ReadingTarget(IReadOnlyDictionary<string, MdRunValue> resolvedParams, string? scriptDirectory)
    {
        var (connectionString, schema) = base.ReadingTarget(resolvedParams, scriptDirectory);
        return (ExpandPassfile(connectionString, Home), schema);
    }

    // The script runs for whoever runs it: "~" is that user's home.
    protected override string ConstructorStatements(MdRunConnection connection, ConnectionCodeContext context) =>
        base.ConstructorStatements(connection, context)
        + "        {\n"
        + "            var builder = new global::Npgsql.NpgsqlConnectionStringBuilder(ConnectionString);\n"
        + "            if (builder.Passfile is { } passfile && (passfile == \"~\" || passfile.StartsWith(\"~/\") || passfile.StartsWith(\"~\\\\\")))\n"
        + "            {\n"
        + "                builder.Passfile = global::System.IO.Path.Join(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.UserProfile),\n"
        + "                    passfile.Substring(1).TrimStart('/', '\\\\'));\n"
        + "                ConnectionString = builder.ConnectionString;\n"
        + "            }\n"
        + "        }\n";

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString());

    protected override void AddDesignTimeServices(IServiceCollection services)
    {
        // The provider's design-time services are internal API by EF Core's own marking; `dotnet ef` registers them the same way.
#pragma warning disable EF1001
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
    }

    protected override string Explain(string connectionString, Exception exception) =>
        Explain(connectionString, exception as NpgsqlException ?? exception.InnerException as NpgsqlException, exception.Message);

    // A refused or missing password says where Invoklet looked for one.
    internal static string Explain(string connectionString, NpgsqlException? failure, string message = "")
    {
        message = failure?.Message ?? message;
        var refused = failure is PostgresException { SqlState: "28P01" or "28000" } || message.Contains("No password has been provided", StringComparison.Ordinal);
        if (!refused) return $"The database can't be read: {message}";
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return string.IsNullOrEmpty(builder.Password)
            ? $"The server asked for a password. The connection string has none, and no line of the pgpass file matches {builder.Host}:{builder.Port}:{builder.Database}:{builder.Username} "
              + $"({DefaultPassfile}, or the file named by Passfile= or PGPASSFILE; each line is host:port:database:user:password). " + message
            : $"The server refused the password of user '{builder.Username}'. {message}";
    }
}
