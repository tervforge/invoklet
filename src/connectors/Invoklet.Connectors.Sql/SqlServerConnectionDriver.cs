using Microsoft.Data.SqlClient;

namespace Invoklet.Connectors.Sql;

/// <summary>A SQL Server database: its tables, views and foreign keys, as a SQL Server Entity Framework Core context.</summary>
public sealed class SqlServerConnectionDriver : SqlConnectionDriver
{
    public const string DriverId = "sqlserver";

    // The version the script's context is built with.
    public const string EntityFrameworkVersion = "10.0.13";

    public override string Id => DriverId;

    public override string DisplayName => "SQL Server";

    internal override string Kind => "SQL Server";

    internal override string DefaultSchema => "dbo";

    internal override string UseCall => "global::Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions.UseSqlServer";

    internal override string ConnectionStringExample => "Server=localhost;Database=Shop;User Id=app;Password=${secret:shop-password};TrustServerCertificate=true";

    public override IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>
    {
        ["Microsoft.EntityFrameworkCore.SqlServer"] = EntityFrameworkVersion,
    };

    public override IReadOnlyDictionary<string, string> PackagesFor(string? targetFramework) => SqlPackages.MajorVersion(targetFramework) switch
    {
        8 => new Dictionary<string, string> { ["Microsoft.EntityFrameworkCore.SqlServer"] = "8.0.33" },
        9 => new Dictionary<string, string> { ["Microsoft.EntityFrameworkCore.SqlServer"] = "9.0.22" },
        _ => Packages,
    };

    internal override string? CheckConnectionString(string text)
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

    internal override async Task<List<SqlTable>> ReadAsync(string connectionString, string? schema, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
            await using var connection = new SqlConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tables = new Dictionary<(string, string), SqlTable>();

            await using (var columns = Command(connection, Columns, schema))
            await using (var reader = await columns.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!tables.TryGetValue(key, out var table))
                        tables[key] = table = new SqlTable(key.Item1, key.Item2, reader.GetString(2).Trim() == "V", []);
                    var type = reader.GetString(4);
                    var (clr, isValue) = SqlServerTypes.Map(type);
                    var generated = reader.GetBoolean(6) ? "OnAdd" : reader.GetBoolean(7) || type is "timestamp" or "rowversion" ? "OnAddOrUpdate" : "None";
                    table.Columns.Add(new SqlColumn(reader.GetString(3), type, clr, isValue, !reader.GetBoolean(5), 0, generated));
                }

            await using (var keys = Command(connection, PrimaryKeys, schema))
            await using (var reader = await keys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    if (tables.TryGetValue((reader.GetString(0), reader.GetString(1)), out var table))
                    {
                        var index = table.Columns.FindIndex(c => c.Name == reader.GetString(2));
                        if (index >= 0) table.Columns[index] = table.Columns[index] with { PrimaryKey = Convert.ToInt32(reader.GetValue(3)), NotNull = true };
                    }

            await using (var foreignKeys = Command(connection, ForeignKeys, schema))
            await using (var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                SqlForeignKey? current = null;
                long currentId = -1;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!tables.TryGetValue((reader.GetString(1), reader.GetString(2)), out var table)) continue;
                    var id = Convert.ToInt64(reader.GetValue(0));
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
        catch (SqlException ex)
        {
            throw new SqlConnectionException($"The database can't be read: {ex.Message}");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException)
        {
            throw new SqlConnectionException($"The database can't be reached: {ex.Message}");
        }
    }

    private static SqlCommand Command(SqlConnection connection, string text, string? schema)
    {
        var command = new SqlCommand(text, connection);
        command.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 128) { Value = (object?)schema ?? DBNull.Value });
        return command;
    }

    private const string Columns = """
        SELECT s.name, o.name, o.type, c.name, ty.name, c.is_nullable, c.is_identity, c.is_computed
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND (@schema IS NULL OR s.name = @schema)
        ORDER BY s.name, o.name, c.column_id
        """;

    private const string PrimaryKeys = """
        SELECT s.name, t.name, col.name, ic.key_ordinal
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE i.is_primary_key = 1 AND (@schema IS NULL OR s.name = @schema)
        ORDER BY s.name, t.name, ic.key_ordinal
        """;

    // One row per column of a foreign key, in the order of the key.
    private const string ForeignKeys = """
        SELECT fk.object_id, s.name, t.name, pc.name, rs.name, rt.name, rc.name
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE @schema IS NULL OR s.name = @schema
        ORDER BY fk.object_id, fkc.constraint_column_id
        """;
}

/// <summary>The C# type of a SQL Server column, by the name of its type, as the SQL Server EF Core provider maps them.</summary>
internal static class SqlServerTypes
{
    public static (string ClrType, bool IsValueType) Map(string typeName) => typeName.ToLowerInvariant() switch
    {
        "int" => ("int", true),
        "bigint" => ("long", true),
        "smallint" => ("short", true),
        "tinyint" => ("byte", true),
        "bit" => ("bool", true),
        "decimal" or "numeric" or "money" or "smallmoney" => ("decimal", true),
        "float" => ("double", true),
        "real" => ("float", true),
        "uniqueidentifier" => ("global::System.Guid", true),
        "datetime" or "datetime2" or "smalldatetime" => ("global::System.DateTime", true),
        "datetimeoffset" => ("global::System.DateTimeOffset", true),
        "date" => ("global::System.DateOnly", true),
        "time" => ("global::System.TimeOnly", true),
        "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => ("byte[]", false),
        // Text, xml, and anything the provider has no plain type for: read as text.
        _ => ("string", false),
    };
}
