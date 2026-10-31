namespace Invoklet.Connectors.Sql;

/// <summary>A table or view of a database server, as the schema reads it; this is also what the connection keeps as its description.</summary>
/// <param name="Schema">The schema it lives in (<c>public</c>, <c>dbo</c>).</param>
internal sealed record SqlTable(string Schema, string Name, bool IsView, List<SqlColumn> Columns)
{
    public List<SqlForeignKey> ForeignKeys { get; init; } = [];
}

/// <param name="Type">The type the server names, for showing.</param>
/// <param name="ClrType">The C# type of the property.</param>
/// <param name="PrimaryKey">Its position in the primary key, from 1; 0 when it isn't part of it.</param>
/// <param name="Generated">None, OnAdd (identity, a sequence) or OnAddOrUpdate (computed, a row version).</param>
/// <param name="ColumnType">The server's own type name, when EF Core must be told (<c>jsonb</c>); null otherwise.</param>
internal sealed record SqlColumn(string Name, string Type, string ClrType, bool IsValueType, bool NotNull, int PrimaryKey,
    string Generated = "None", string? ColumnType = null);

internal sealed record SqlForeignKey(string ReferencedSchema, string Table, List<string> Columns, List<string> ReferencedColumns);

/// <summary>A failure to read the database, in words for the user.</summary>
internal sealed class SqlConnectionException(string message) : Exception(message);
