using Invoklet.Connectors.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Invoklet.NonUI.Tests.Connectors.EfCore.Fixtures;

// A SQLite file standing in for a server: the same reverse engineering and the same generated context, with a provider that needs no server.
internal sealed class SqliteEfDriver : EfCoreConnectionDriver
{
    public override string Id => "sqliteef";

    public override string DisplayName => "SQLite (EF Core)";

    protected override string Kind => "SQLite";

    protected override string UseCall => "global::Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite";

    protected override string ConnectionStringExample => "Data Source=shop.db";

    protected override string AuthenticationHint => "";

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string> { ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.13" };

    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => Packages;

    protected override string? CheckConnectionString(string text) => null;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString) => options.UseSqlite(connectionString);

    protected override void AddDesignTimeServices(IServiceCollection services)
    {
        // The provider's design-time services are internal API by EF Core's own marking; `dotnet ef` registers them the same way.
#pragma warning disable EF1001
        new SqliteDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
    }
}
