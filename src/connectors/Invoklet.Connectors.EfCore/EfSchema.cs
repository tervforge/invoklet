namespace Invoklet.Connectors.EfCore;

/// <summary>A table or view as Entity Framework Core's reverse engineering made it; this is also what the connection keeps as its description.</summary>
/// <param name="Schema">The schema it lives in (<c>public</c>, <c>dbo</c>); empty when the database has none.</param>
/// <param name="Name">The table or view in the database.</param>
/// <param name="ClassName">The name of its row class.</param>
/// <param name="SetName">The name of its set on the context.</param>
internal sealed record EfEntity(string Schema, string Name, bool IsView, string ClassName, string SetName, List<EfColumn> Columns)
{
    public List<EfForeignKey> ForeignKeys { get; init; } = [];
}

/// <param name="Name">The column in the database.</param>
/// <param name="Property">The property of the row class.</param>
/// <param name="Type">The type the database names, for showing.</param>
/// <param name="ClrType">The C# type of the property, without the question mark of a nullable one.</param>
/// <param name="PrimaryKey">Its position in the primary key, from 1; 0 when it isn't part of it.</param>
/// <param name="Generated">Never, OnAdd (identity, a sequence) or OnAddOrUpdate (computed, a row version).</param>
/// <param name="ColumnType">The database's own type, when the context must say it (<c>jsonb</c>, <c>decimal(19,4)</c>); null otherwise.</param>
/// <param name="ConcurrencyToken">The column's value is part of the condition of an update (a SQL Server <c>rowversion</c>), so an update of a row another client has changed since it was read fails.</param>
internal sealed record EfColumn(string Name, string Property, string Type, string ClrType, bool IsValueType, bool NotNull, int PrimaryKey,
    string Generated = "Never", string? ColumnType = null, string? DefaultSql = null, string? ComputedSql = null, bool ConcurrencyToken = false);

/// <param name="ReferencedSchema">The schema of the table the key points to.</param>
/// <param name="Table">The table the key points to.</param>
/// <param name="Columns">The columns of this table, as the database names them.</param>
/// <param name="ReferencedColumns">The columns they point to.</param>
/// <param name="PrincipalClass">The row class of the table the key points to.</param>
/// <param name="Properties">The properties of this row class that hold the key.</param>
/// <param name="PrincipalProperties">The properties of the other row class they point to.</param>
/// <param name="Reference">The navigation property on this row class, null when the model has none.</param>
/// <param name="Collection">The navigation property on the other row class, null when the model has none: a collection of the rows that point to it, or the one row when <paramref name="IsUnique"/>.</param>
/// <param name="IsUnique">At most one row points to a row of the other table (the key is unique): a one-to-one relationship, not one-to-many.</param>
internal sealed record EfForeignKey(string ReferencedSchema, string Table, List<string> Columns, List<string> ReferencedColumns, string PrincipalClass,
    List<string> Properties, List<string> PrincipalProperties, string? Reference, string? Collection, bool IsUnique = false);

/// <summary>A failure to read the database, in words for the user.</summary>
public sealed class EfConnectionException(string message) : Exception(message);
