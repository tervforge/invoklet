using Invoklet.Connectors.EfCore;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Invoklet.Connectors.Sqlite;

/// <summary>
/// A SQLite database file. Invoklet reads its tables and views with Entity Framework Core's reverse engineering, and the script gets an EF Core context with a
/// set per table, as LINQPad gives a typed data context: <c>Connections.Shop.Customers.Where(c => c.City == "Oslo")</c>. A relative path is read from
/// the script's folder.
/// </summary>
public sealed class SqliteConnectionDriver : EfCoreConnectionDriver
{
    public const string DriverId = "sqlite";

    // The version the script's context is built with.
    public const string EntityFrameworkVersion = "10.0.13";

    private const string Package = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string File = "file";
    private const string ReadOnly = "readOnly";

    public override string Id => DriverId;

    public override string DisplayName => "SQLite";

    // The description is the model of EF Core now, not the tables of the file as SQLite lists them.
    public override string SchemaVersion => "6";

    protected override string Kind => "SQLite";

    protected override string UseCall => "global::Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite";

    protected override string ConnectionStringExample => "Data Source=data/shop.db";

    protected override string AuthenticationHint => "";

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string> { [Package] = EntityFrameworkVersion };

    // EF Core 10 needs .NET 10: a script on an older framework gets the newest release that runs there.
    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => MajorVersion(targetFramework) switch
    {
        8 => new Dictionary<string, string> { [Package] = "8.0.33" },
        9 => new Dictionary<string, string> { [Package] = "9.0.22" },
        _ => Packages,
    };

    public override IReadOnlyList<ConnectionParam> Params { get; } =
    [
        new(File, "Database file", ConnectionParamType.File, Required: true, Example: "data/shop.db",
            Description: "A relative path is read from the script's folder. The file must exist."),
        new(ReadOnly, "Read only", ConnectionParamType.Boolean, Default: "false",
            Description: "Opens the file for reading only, so the script can't change it."),
    ];

    public override IReadOnlyList<string> Validate(MdRunConnection connection)
    {
        var errors = new List<string>();
        if (!connection.Params.TryGetValue(File, out var file) || string.IsNullOrWhiteSpace(file.Text))
            errors.Add("The database file is missing.");
        else if (file.Text.Trim() == ":memory:")
            errors.Add("An in-memory database has no tables to read: give a database file.");
        if (connection.Params.TryGetValue(ReadOnly, out var readOnly) && readOnly.Text is { Length: > 0 } flag && !bool.TryParse(flag, out _))
            errors.Add($"Read only must be true or false, not '{flag}'.");
        return errors;
    }

    // A relative path, or one a placeholder may make relative, depends on the script's folder.
    public override bool UsesScriptDirectory(MdRunConnection connection) =>
        connection.Params.TryGetValue(File, out var file) && file.Text is { } text
        && (MdRunPlaceholders.Parse(text).Parts.Any(p => p.Kind != MdRunPlaceholders.Kind.Text) || !Path.IsPathRooted(text));

    protected override string? CheckConnectionString(string text) => null;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString) => options.UseSqlite(connectionString);

    protected override void AddDesignTimeServices(IServiceCollection services)
    {
        // The provider's design-time services are internal API by EF Core's own marking; `dotnet ef` registers them the same way.
#pragma warning disable EF1001
        new SqliteDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
    }

    // SQLite lets a primary key column hold NULL, unless the column says NOT NULL, the table is STRICT or WITHOUT ROWID, or the key is a rowid alias
    // (a lone INTEGER PRIMARY KEY). EF Core reports every key column as required and fails to read a row with a NULL in one, so such a table is read as
    // a table without a key: it can be queried but not changed.
    protected override void AdjustDatabaseModel(Microsoft.EntityFrameworkCore.Scaffolding.Metadata.DatabaseModel database, string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        foreach (var table in database.Tables)
        {
            if (table.PrimaryKey is not { } key || table.Name.StartsWith("sqlite_", StringComparison.Ordinal)) continue;
            var nullable = NullableKeyColumns(connection, table.Name);
            if (nullable.Count == 0) continue;
            // The columns are read as the file declares them, which for these says they can be NULL.
            foreach (var column in key.Columns.Where(c => nullable.Contains(c.Name))) column.IsNullable = true;
            table.PrimaryKey = null;
        }
    }

    // The primary key columns that can hold NULL; none when the table can't hold one in its key.
    private static HashSet<string> NullableKeyColumns(SqliteConnection connection, string table)
    {
        using var kind = connection.CreateCommand();
        kind.CommandText = "SELECT wr, strict FROM pragma_table_list($table) WHERE schema = 'main'";
        kind.Parameters.AddWithValue("$table", table);
        using (var reader = kind.ExecuteReader())
            if (reader.Read() && (reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0)) return [];

        using var indexes = connection.CreateCommand();
        indexes.CommandText = "SELECT count(*) FROM pragma_index_list($table) WHERE origin = 'pk'";
        indexes.Parameters.AddWithValue("$table", table);
        var hasKeyIndex = Convert.ToInt64(indexes.ExecuteScalar()) > 0;

        using var info = connection.CreateCommand();
        info.CommandText = "SELECT name, type, \"notnull\" FROM pragma_table_xinfo($table) WHERE pk > 0";
        info.Parameters.AddWithValue("$table", table);
        var columns = new List<(string Name, string Type, bool NotNull)>();
        using (var reader = info.ExecuteReader())
            while (reader.Read()) columns.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.GetInt64(2) != 0));
        // A lone INTEGER PRIMARY KEY (not DESC) aliases the rowid and has no key index: it can't be NULL.
        if (!hasKeyIndex && columns is [var only] && only.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)) return [];
        return [.. columns.Where(c => !c.NotNull).Select(c => c.Name)];
    }

    // The file is opened for reading only, so that Invoklet never creates or changes it.
    protected override (string ConnectionString, string? Schema) ReadingTarget(IReadOnlyDictionary<string, MdRunValue> resolvedParams, string? scriptDirectory)
    {
        if (!resolvedParams.TryGetValue(File, out var fileValue) || fileValue.Text is not { Length: > 0 } text)
            throw new EfConnectionException("The database file is missing.");
        var file = text.Trim();
        if (!Path.IsPathRooted(file))
        {
            if (scriptDirectory is null)
                throw new EfConnectionException($"'{file}' is relative to the script's folder: save the script first, or give a full path.");
            file = Path.GetFullPath(file, scriptDirectory);
        }
        if (!System.IO.File.Exists(file))
            throw new EfConnectionException($"The database file {file} doesn't exist.");
        return (new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString(), null);
    }

    protected override string Explain(string connectionString, Exception exception) =>
        $"The database file {new SqliteConnectionStringBuilder(connectionString).DataSource} can't be read: {exception.InnerException?.Message ?? exception.Message}";

    // A DbContext opening the file, a relative path taken from the script's folder.
    protected override string ConstructorStatements(MdRunConnection connection, ConnectionCodeContext context)
    {
        var file = connection.Params[File].Text!.Trim();
        var readOnly = connection.Params.TryGetValue(ReadOnly, out var r) && bool.TryParse(r.Text, out var flag) && flag;
        const string io = "global::System.IO.Path";
        return "        var file = " + context.Text(file) + ";\n"
            + "        if (!" + io + ".IsPathRooted(file))\n"
            + "            file = " + io + ".GetFullPath(file, " + io + ".GetDirectoryName(global::Util.CurrentQueryPath)\n"
            + "                ?? throw new global::System.InvalidOperationException("
            + ConnectionCodeContext.Literal($"Connection '{connection.Name}': the database file '{file}' is relative to the script's folder: save the script first, or give a full path.")
            + "));\n"
            + "        ConnectionString = new global::Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = file, Mode = "
            + "global::Microsoft.Data.Sqlite.SqliteOpenMode." + (readOnly ? "ReadOnly" : "ReadWrite") + " }.ToString();\n";
    }
}
