using System.Text.Json;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;

namespace Invoklet.Connectors.Sql;

/// <summary>
/// A database server: Invoklet reads its tables and views, and the script gets an Entity Framework Core context with a set per table, as LINQPad
/// gives a typed data context: <c>Connections.Shop.Customers.Where(c => c.City == "Oslo")</c>. The connection is one connection string, with
/// <c>${secret:…}</c> for the password; an optional schema limits what is read to it.
/// </summary>
public abstract class SqlConnectionDriver : IConnectionSchemaDriver
{
    private const string ConnectionStringKey = "connectionString";
    private const string SchemaKey = "schema";

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public ConnectionCategory Category => ConnectionCategory.Database;

    public string SchemaVersion => "1";

    public abstract IReadOnlyDictionary<string, string> Packages { get; }

    public abstract IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework);

    internal abstract string Kind { get; }

    // The schema a table is named without, and the EF Core call that opens the database.
    internal abstract string DefaultSchema { get; }

    internal abstract string UseCall { get; }

    internal abstract string ConnectionStringExample { get; }

    // Null when the text is a connection string of this server, otherwise why it is not.
    internal abstract string? CheckConnectionString(string text);

    internal abstract Task<List<SqlTable>> ReadAsync(string connectionString, string? schema, CancellationToken cancellationToken);

    public IReadOnlyList<ConnectionParam> Params =>
    [
        new(ConnectionStringKey, "Connection string", Required: true, Example: ConnectionStringExample,
            Description: "Write the password as ${secret:name} to keep it in the Secret Store."),
        new(SchemaKey, "Schema", Description: "Optional. Read only the tables of this schema; without it all schemas are read."),
    ];

    public IReadOnlyList<string> Validate(MdRunConnection connection)
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
            return new ConnectionTestResult(true, Summary(await ReadTablesAsync(resolvedParams, cancellationToken).ConfigureAwait(false)));
        }
        catch (SqlConnectionException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<ConnectionSchema> FetchSchemaAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools,
        CancellationToken cancellationToken)
    {
        var tables = await ReadTablesAsync(resolvedParams, cancellationToken).ConfigureAwait(false);
        return new ConnectionSchema(Summary(tables), [], JsonSerializer.Serialize(tables));
    }

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context)
    {
        var tables = context.Schema is { } schema ? JsonSerializer.Deserialize<List<SqlTable>>(schema.Data) ?? [] : [];
        return new ConnectionClientCode(SqlContextCode.Generate(connection, context, Kind, connection.Params[ConnectionStringKey].Text!.Trim(), UseCall, DefaultSchema, tables));
    }

    private async Task<List<SqlTable>> ReadTablesAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken)
    {
        if (!resolvedParams.TryGetValue(ConnectionStringKey, out var text) || text.Text is not { Length: > 0 } connectionString)
            throw new SqlConnectionException("The connection string is missing.");
        if (CheckConnectionString(connectionString) is { } problem) throw new SqlConnectionException(problem);
        var schema = resolvedParams.TryGetValue(SchemaKey, out var s) && !string.IsNullOrWhiteSpace(s.Text) ? s.Text.Trim() : null;
        return await ReadAsync(connectionString, schema, cancellationToken).ConfigureAwait(false);
    }

    internal static string Summary(IReadOnlyList<SqlTable> tables)
    {
        if (tables.Count == 0) return "The database has no tables yet.";
        var tableCount = tables.Count(t => !t.IsView);
        var viewCount = tables.Count - tableCount;
        var counts = string.Join(" and ", new[]
        {
            tableCount == 0 ? null : tableCount == 1 ? "1 table" : $"{tableCount} tables",
            viewCount == 0 ? null : viewCount == 1 ? "1 view" : $"{viewCount} views",
        }.OfType<string>());
        const int shown = 10;
        return $"{counts}: {string.Join(", ", tables.Take(shown).Select(t => t.Name))}{(tables.Count > shown ? ", …" : "")}.";
    }

    private static bool HasPlaceholder(string text) => MdRunPlaceholders.Parse(text).Parts.Any(p => p.Kind != MdRunPlaceholders.Kind.Text);
}
