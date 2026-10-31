using Npgsql;

namespace Invoklet.Connectors.Sql;

/// <summary>A PostgreSQL database: its tables, views and foreign keys, as an Npgsql Entity Framework Core context.</summary>
public sealed class PostgresConnectionDriver : SqlConnectionDriver
{
    public const string DriverId = "postgres";

    // The version the script's context is built with.
    public const string EntityFrameworkVersion = "10.0.3";

    public override string Id => DriverId;

    public override string DisplayName => "PostgreSQL";

    internal override string Kind => "PostgreSQL";

    internal override string DefaultSchema => "public";

    internal override string UseCall => "global::Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql";

    internal override string ConnectionStringExample => "Host=localhost;Database=shop;Username=app;Password=${secret:shop-password}";

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>
    {
        ["Npgsql.EntityFrameworkCore.PostgreSQL"] = EntityFrameworkVersion,
    };

    // The provider's release for the EF Core of the script's framework.
    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => SqlPackages.MajorVersion(targetFramework) switch
    {
        8 => new Dictionary<string, string> { ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "8.0.11" },
        9 => new Dictionary<string, string> { ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "9.0.4" },
        _ => Packages,
    };

    internal override string? CheckConnectionString(string text)
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

    internal override async Task<List<SqlTable>> ReadAsync(string connectionString, string? schema, CancellationToken cancellationToken)
    {
        try
        {
            // Pooling would keep the connection open after the read.
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tables = new Dictionary<(string, string), SqlTable>();

            await using (var columns = Command(connection, Columns, schema))
            await using (var reader = await columns.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!tables.TryGetValue(key, out var table))
                        tables[key] = table = new SqlTable(key.Item1, key.Item2, reader.GetString(2) == "VIEW", []);
                    var (clr, isValue, columnType) = PostgresTypes.Map(reader.GetString(4));
                    // An identity column, or one filled from a sequence (serial), is made by the server on insert; a generated column on every write.
                    var generated = reader.GetString(6) == "YES" ? "OnAdd"
                        : reader.GetString(7) == "ALWAYS" ? "OnAddOrUpdate"
                        : !reader.IsDBNull(8) && reader.GetString(8).StartsWith("nextval(", StringComparison.Ordinal) ? "OnAdd" : "None";
                    table.Columns.Add(new SqlColumn(reader.GetString(3), reader.GetString(4), clr, isValue, reader.GetString(5) == "NO", 0, generated, columnType));
                }

            await using (var keys = Command(connection, PrimaryKeys, schema))
            await using (var reader = await keys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    if (tables.TryGetValue((reader.GetString(0), reader.GetString(1)), out var table))
                    {
                        var index = table.Columns.FindIndex(c => c.Name == reader.GetString(2));
                        if (index >= 0) table.Columns[index] = table.Columns[index] with { PrimaryKey = reader.GetInt32(3), NotNull = true };
                    }

            await using (var foreignKeys = Command(connection, ForeignKeys, schema))
            await using (var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                SqlForeignKey? current = null;
                long currentId = -1;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!tables.TryGetValue((reader.GetString(1), reader.GetString(2)), out var table)) continue;
                    var id = reader.GetInt64(0);
                    if (current is null || id != currentId)
                    {
                        current = new SqlForeignKey(reader.GetString(4), reader.GetString(5), [], []);
                        currentId = id;
                        table.ForeignKeys.Add(current);
                    }
                    current.Columns.Add(reader.GetString(3));
                    current.ReferencedColumns.Add(reader.GetString(6));
                }
            }
            return [.. tables.Values.OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal)];
        }
        catch (NpgsqlException ex)
        {
            throw new SqlConnectionException($"The database can't be read: {ex.Message}");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException)
        {
            throw new SqlConnectionException($"The database can't be reached: {ex.Message}");
        }
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string text, string? schema)
    {
        var command = new NpgsqlCommand(text, connection);
        command.Parameters.Add(new NpgsqlParameter("schema", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schema ?? DBNull.Value });
        return command;
    }

    private const string Columns = """
        SELECT c.table_schema, c.table_name, t.table_type, c.column_name, c.udt_name, c.is_nullable, c.is_identity, c.is_generated, c.column_default
        FROM information_schema.columns c
        JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema') AND t.table_type IN ('BASE TABLE', 'VIEW')
          AND (@schema::text IS NULL OR c.table_schema = @schema::text)
        ORDER BY c.table_schema, c.table_name, c.ordinal_position
        """;

    private const string PrimaryKeys = """
        SELECT kcu.table_schema, kcu.table_name, kcu.column_name, kcu.ordinal_position
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
          ON kcu.constraint_schema = tc.constraint_schema AND kcu.constraint_name = tc.constraint_name AND kcu.table_name = tc.table_name
        WHERE tc.constraint_type = 'PRIMARY KEY' AND (@schema::text IS NULL OR tc.table_schema = @schema::text)
        ORDER BY kcu.table_schema, kcu.table_name, kcu.ordinal_position
        """;

    // One row per column of a foreign key, in the order of the key.
    private const string ForeignKeys = """
        SELECT con.oid::bigint, n.nspname, c.relname, a.attname, fn.nspname, fc.relname, fa.attname
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_class fc ON fc.oid = con.confrelid
        JOIN pg_namespace fn ON fn.oid = fc.relnamespace
        CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, fattnum, ord)
        JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
        JOIN pg_attribute fa ON fa.attrelid = con.confrelid AND fa.attnum = k.fattnum
        WHERE con.contype = 'f' AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND (@schema::text IS NULL OR n.nspname = @schema::text)
        ORDER BY con.oid, k.ord
        """;
}

/// <summary>The C# type of a PostgreSQL column, by the name of its type (<c>udt_name</c>), as the Npgsql EF Core provider maps them.</summary>
internal static class PostgresTypes
{
    public static (string ClrType, bool IsValueType, string? ColumnType) Map(string udtName)
    {
        // An array type is named with a leading underscore after its element.
        if (udtName.StartsWith('_') && udtName.Length > 1)
        {
            var (element, _, _) = Map(udtName[1..]);
            return (element + "[]", false, null);
        }
        return udtName switch
        {
            "int2" => ("short", true, null),
            "int4" => ("int", true, null),
            "int8" => ("long", true, null),
            "bool" => ("bool", true, null),
            "float4" => ("float", true, null),
            "float8" => ("double", true, null),
            "numeric" or "money" => ("decimal", true, null),
            "uuid" => ("global::System.Guid", true, null),
            "timestamp" => ("global::System.DateTime", true, null),
            "timestamptz" => ("global::System.DateTimeOffset", true, null),
            "date" => ("global::System.DateOnly", true, null),
            "time" => ("global::System.TimeOnly", true, null),
            "interval" => ("global::System.TimeSpan", true, null),
            "bytea" => ("byte[]", false, null),
            "json" or "jsonb" => ("string", false, udtName),
            // Text, and anything the provider has no plain type for: read as text.
            _ => ("string", false, null),
        };
    }
}

/// <summary>The EF Core provider of a script's target framework.</summary>
internal static class SqlPackages
{
    // "net8.0" is 8; "latest", a missing value or anything else is not an older framework.
    public static int MajorVersion(string? targetFramework) =>
        targetFramework is { Length: > 3 } tfm && tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(tfm[3..].Split('.')[0], out var major) ? major : 0;
}
