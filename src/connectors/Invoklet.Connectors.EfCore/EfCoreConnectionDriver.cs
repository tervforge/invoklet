using System.Text.Json;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Invoklet.Connectors.EfCore;

/// <summary>
/// A database server read with Entity Framework Core's reverse engineering. The script gets an EF Core context with a set per table, as LINQPad gives a
/// typed data context: <c>Connections.Shop.Customers.Where(c => c.City == "Oslo")</c>. The connection is one connection string, with <c>${secret:…}</c> for
/// the password; an optional schema limits what is read to it. A provider (PostgreSQL, SQL Server) is a library of its own that derives from this class
/// and brings its own packages.
/// </summary>
public abstract class EfCoreConnectionDriver : IConnectionSchemaDriver
{
    private const string ConnectionStringKey = "connectionString";
    private const string SchemaKey = "schema";

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public ConnectionCategory Category => ConnectionCategory.Database;

    /// <summary>Change when cached descriptions need to be read again after a driver update.</summary>
    public virtual string SchemaVersion => "1";

    /// <summary>The provider's EF Core package in the release Invoklet is built with.</summary>
    public abstract IReadOnlyDictionary<string, string> Packages { get; }

    /// <summary>The provider's release for the EF Core of a script's target framework (<c>net8.0</c>, <c>latest</c>).</summary>
    public abstract IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework);

    /// <summary>The name of the server in the documentation of the generated context.</summary>
    protected abstract string Kind { get; }

    /// <summary>The EF Core call that opens the database: <c>global::…UseNpgsql</c>, taking the options builder and the connection string.</summary>
    protected abstract string UseCall { get; }

    protected abstract string ConnectionStringExample { get; }

    /// <summary>What the form says about signing in without a password in the text.</summary>
    protected abstract string AuthenticationHint { get; }

    /// <summary>Null when the text is a connection string of this server, otherwise why it is not.</summary>
    protected abstract string? CheckConnectionString(string text);

    /// <summary>Configures the provider on the context the reverse engineering is built on.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder options, string connectionString);

    /// <summary>Adds the provider's design-time services (its <c>IDesignTimeServices</c>).</summary>
    protected abstract void AddDesignTimeServices(IServiceCollection services);

    /// <summary>Changes what the database was read as before the model is made from it: a provider corrects what it knows its database reports wrongly.</summary>
    protected virtual void AdjustDatabaseModel(Microsoft.EntityFrameworkCore.Scaffolding.Metadata.DatabaseModel database, string connectionString)
    {
    }

    /// <summary>The words for a failure to read the database; a driver adds what it knows about signing in.</summary>
    protected virtual string Explain(string connectionString, Exception exception) => $"The database can't be read: {exception.Message}";

    public virtual IReadOnlyList<ConnectionParam> Params =>
    [
        new(ConnectionStringKey, "Connection string", Required: true, Example: ConnectionStringExample,
            Description: "Write the password as ${secret:name} to keep it in the Secret Store. " + AuthenticationHint),
        new(SchemaKey, "Schema", Description: "Optional. Read only the tables of this schema; without it all schemas are read."),
    ];

    /// <summary>Whether the description depends on the script's folder, as a database file given by a relative path does.</summary>
    public virtual bool UsesScriptDirectory(MdRunConnection connection) => false;

    /// <summary>
    /// What the database is read with: the connection string and the schema to limit it to, from the connection's parameters with the placeholders replaced.
    /// Throws <see cref="EfConnectionException"/> with the words for the user when the parameters can't name a database.
    /// </summary>
    protected virtual (string ConnectionString, string? Schema) ReadingTarget(IReadOnlyDictionary<string, MdRunValue> resolvedParams, string? scriptDirectory)
    {
        if (!resolvedParams.TryGetValue(ConnectionStringKey, out var text) || text.Text is not { Length: > 0 } connectionString)
            throw new EfConnectionException("The connection string is missing.");
        if (CheckConnectionString(connectionString) is { } problem) throw new EfConnectionException(problem);
        var schema = resolvedParams.TryGetValue(SchemaKey, out var s) && !string.IsNullOrWhiteSpace(s.Text) ? s.Text.Trim() : null;
        return (connectionString, schema);
    }

    /// <summary>The statements of the generated context's constructor that assign <c>ConnectionString</c>.</summary>
    protected virtual string ConstructorStatements(MdRunConnection connection, ConnectionCodeContext context) =>
        "        ConnectionString = " + context.Text(connection.Params[ConnectionStringKey].Text!.Trim()) + ";\n";

    public virtual IReadOnlyList<string> Validate(MdRunConnection connection)
    {
        var errors = new List<string>();
        if (!connection.Params.TryGetValue(ConnectionStringKey, out var text) || string.IsNullOrWhiteSpace(text.Text))
            errors.Add("The connection string is missing.");
        else if (!HasPlaceholder(text.Text) && CheckConnectionString(text.Text) is { } problem)
            errors.Add(problem);
        return errors;
    }

    public async Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken)
    {
        try
        {
            return new ConnectionTestResult(true, Summary(await ReadAsync(resolvedParams, null, cancellationToken).ConfigureAwait(false)));
        }
        catch (EfConnectionException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<ConnectionSchema> FetchSchemaAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools,
        CancellationToken cancellationToken)
    {
        var entities = await ReadAsync(resolvedParams, tools.ScriptDirectory, cancellationToken).ConfigureAwait(false);
        return new ConnectionSchema(Summary(entities), [], JsonSerializer.Serialize(entities));
    }

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context)
    {
        var entities = context.Schema is { } schema ? JsonSerializer.Deserialize<List<EfEntity>>(schema.Data) ?? [] : [];
        return new ConnectionClientCode(EfContextCode.Generate(connection, context, Kind, ConstructorStatements(connection, context), UseCall, entities));
    }

    private async Task<List<EfEntity>> ReadAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, string? scriptDirectory, CancellationToken cancellationToken)
    {
        var (connectionString, schema) = ReadingTarget(resolvedParams, scriptDirectory);
        try
        {
            // The reverse engineering is synchronous: it runs on the thread pool and is abandoned, not cancelled, when the caller gives up.
            return await Task.Run(() => EfModelReader.Read(connectionString, schema, UseProvider, AddDesignTimeServices, AdjustDatabaseModel), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EfConnectionException(Explain(connectionString, ex));
        }
    }

    internal static string Summary(IReadOnlyList<EfEntity> entities)
    {
        if (entities.Count == 0) return "The database has no tables yet.";
        var tableCount = entities.Count(t => !t.IsView);
        var viewCount = entities.Count - tableCount;
        var counts = string.Join(" and ", new[]
        {
            tableCount == 0 ? null : tableCount == 1 ? "1 table" : $"{tableCount} tables",
            viewCount == 0 ? null : viewCount == 1 ? "1 view" : $"{viewCount} views",
        }.OfType<string>());
        const int shown = 10;
        var names = entities.Select(t => t.Schema.Length > 0 && entities.Count(o => o.Name == t.Name) > 1 ? t.Schema + "." + t.Name : t.Name);
        return $"{counts}: {string.Join(", ", names.Take(shown))}{(entities.Count > shown ? ", …" : "")}.";
    }

    /// <summary>"net8.0" is 8; "latest", a missing value or anything else is not an older framework.</summary>
    protected static int MajorVersion(string? targetFramework) =>
        targetFramework is { Length: > 3 } tfm && tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(tfm[3..].Split('.')[0], out var major) ? major : 0;

    private static bool HasPlaceholder(string text) => MdRunPlaceholders.Parse(text).Parts.Any(p => p.Kind != MdRunPlaceholders.Kind.Text);
}
