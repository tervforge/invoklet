using Invoklet.Connectors.EfCore;
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

    protected override string ConnectionStringExample => "Host=localhost;Database=shop;Username=app;Password=${secret:shop-password}";

    protected override string AuthenticationHint =>
        "Leave the password out to take it from your pgpass file (~/.pgpass, or the file named by Passfile= or PGPASSFILE).";

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
              + "(~/.pgpass, or the file named by Passfile= or PGPASSFILE; each line is host:port:database:user:password). " + message
            : $"The server refused the password of user '{builder.Username}'. {message}";
    }
}
