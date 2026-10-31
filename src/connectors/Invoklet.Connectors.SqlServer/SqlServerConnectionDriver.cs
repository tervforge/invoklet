using Invoklet.Connectors.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Invoklet.Connectors.SqlServer;

/// <summary>A SQL Server database, read with the SQL Server provider's reverse engineering and used by the script through the same provider.</summary>
public sealed class SqlServerConnectionDriver : EfCoreConnectionDriver
{
    public const string DriverId = "sqlserver";

    // The version the script's context is built with.
    public const string EntityFrameworkVersion = "10.0.13";

    private const string Package = "Microsoft.EntityFrameworkCore.SqlServer";

    public override string Id => DriverId;

    public override string DisplayName => "SQL Server";

    protected override string Kind => "SQL Server";

    protected override string UseCall => "global::Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions.UseSqlServer";

    protected override string ConnectionStringExample => "Server=localhost;Database=Shop;User Id=app;Password=${secret:shop-password};TrustServerCertificate=true";

    protected override string AuthenticationHint =>
        "For your Windows account use Integrated Security=true (on macOS and Linux it needs a Kerberos ticket: kinit).";

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string> { [Package] = EntityFrameworkVersion };

    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => MajorVersion(targetFramework) switch
    {
        8 => new Dictionary<string, string> { [Package] = "8.0.33" },
        9 => new Dictionary<string, string> { [Package] = "9.0.22" },
        _ => Packages,
    };

    protected override string? CheckConnectionString(string text)
    {
        try
        {
            _ = new SqlConnectionStringBuilder(text);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return $"The connection string is not a SQL Server one: {ex.Message}";
        }
    }

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString());

    protected override void AddDesignTimeServices(IServiceCollection services)
    {
        // The provider's design-time services are internal API by EF Core's own marking; `dotnet ef` registers them the same way.
#pragma warning disable EF1001
        new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
    }

    protected override string Explain(string connectionString, Exception exception) => Explain(connectionString, exception.Message);

    // Integrated security signs in as the Windows account; elsewhere it depends on Kerberos, which is the usual reason it fails.
    internal static string Explain(string connectionString, string message)
    {
        var integrated = new SqlConnectionStringBuilder(connectionString).IntegratedSecurity;
        return integrated && !OperatingSystem.IsWindows()
            ? $"The database can't be read: {message} Integrated Security signs in with a Kerberos ticket on macOS and Linux: get one with kinit, or use a user name and password."
            : $"The database can't be read: {message}";
    }
}
