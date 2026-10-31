using System.Text;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;

namespace Invoklet.Connectors.Sql;

/// <summary>The <c>DbContext</c> of a connection: a set per table and view, each row a nested class, the keys and the navigation properties.</summary>
internal static class SqlContextCode
{
    /// <param name="useCall">The EF Core provider's extension that takes the options builder and the connection string.</param>
    public static string Generate(MdRunConnection connection, ConnectionCodeContext context, string kind, string connectionString, string useCall,
        string defaultSchema, IReadOnlyList<SqlTable> tables)
    {
        var model = SqlModel.Build(tables, context.ClassName, defaultSchema);
        const string ef = "global::Microsoft.EntityFrameworkCore";

        var sb = new StringBuilder()
            .Append("/// <summary>The ").Append(kind).Append(" connection '").Append(ConnectionCodeContext.Doc(connection.Name))
            .Append("': an Entity Framework Core context with a set per table.</summary>\n")
            .Append("public sealed class ").Append(context.ClassName).Append(" : ").Append(ef).Append(".DbContext\n{\n")
            .Append("    internal ").Append(context.ClassName).Append("()\n    {\n")
            .Append("        ConnectionString = ").Append(context.Text(connectionString)).Append(";\n")
            .Append("    }\n\n")
            .Append("    /// <summary>What the context opens the database with.</summary>\n")
            .Append("    public string ConnectionString { get; }\n\n")
            .Append("    protected override void OnConfiguring(").Append(ef).Append(".DbContextOptionsBuilder optionsBuilder) =>\n")
            .Append("        ").Append(useCall).Append("(optionsBuilder, ConnectionString);\n");

        foreach (var entity in model)
            sb.Append('\n')
                .Append("    /// <summary>The ").Append(entity.Table.IsView ? "view " : "table ").Append(ConnectionCodeContext.Doc(entity.Table.Schema + "." + entity.Table.Name))
                .Append(entity.Keyless ? " (read only: it has no primary key)" : "").Append(".</summary>\n")
                .Append("    public ").Append(ef).Append(".DbSet<").Append(entity.ClassName).Append("> ").Append(entity.SetName)
                .Append(" => Set<").Append(entity.ClassName).Append(">();\n");

        sb.Append('\n')
            .Append("    protected override void OnModelCreating(").Append(ef).Append(".ModelBuilder modelBuilder)\n    {\n");
        foreach (var entity in model)
        {
            sb.Append("        modelBuilder.Entity<").Append(entity.ClassName).Append(">(e =>\n        {\n")
                .Append("            ").Append(ef).Append(".RelationalEntityTypeBuilderExtensions.").Append(entity.Table.IsView ? "ToView" : "ToTable")
                .Append("(e, ").Append(ConnectionCodeContext.Literal(entity.Table.Name)).Append(", ").Append(ConnectionCodeContext.Literal(entity.Table.Schema)).Append(");\n");
            if (entity.Keyless) sb.Append("            e.HasNoKey();\n");
            else if (entity.Key.Count == 1) sb.Append("            e.HasKey(x => x.").Append(entity.Key[0]).Append(");\n");
            else sb.Append("            e.HasKey(x => new { ").Append(string.Join(", ", entity.Key.Select(k => "x." + k))).Append(" });\n");
            foreach (var column in entity.Columns)
            {
                sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasColumnName(e.Property(x => x.")
                    .Append(column.PropertyName).Append("), ").Append(ConnectionCodeContext.Literal(column.Column.Name)).Append(");\n");
                if (column.Column.ColumnType is { } columnType)
                    sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasColumnType(e.Property(x => x.")
                        .Append(column.PropertyName).Append("), ").Append(ConnectionCodeContext.Literal(columnType)).Append(");\n");
                if (column.Column.Generated == "OnAdd")
                    sb.Append("            e.Property(x => x.").Append(column.PropertyName).Append(").ValueGeneratedOnAdd();\n");
                else if (column.Column.Generated == "OnAddOrUpdate")
                    sb.Append("            e.Property(x => x.").Append(column.PropertyName).Append(").ValueGeneratedOnAddOrUpdate();\n");
            }
            foreach (var relation in entity.Relations)
                sb.Append("            e.HasOne(x => x.").Append(relation.ReferenceName).Append(").WithMany(x => x.").Append(relation.CollectionName)
                    .Append(").HasForeignKey(").Append(Key(relation.ForeignKey)).Append(").HasPrincipalKey(").Append(Key(relation.PrincipalKey)).Append(");\n");
            sb.Append("        });\n");
        }
        sb.Append("    }\n");

        foreach (var entity in model)
        {
            sb.Append('\n')
                .Append("    /// <summary>A row of the ").Append(entity.Table.IsView ? "view " : "table ").Append(ConnectionCodeContext.Doc(entity.Table.Schema + "." + entity.Table.Name))
                .Append(".</summary>\n")
                .Append("    public sealed class ").Append(entity.ClassName).Append("\n    {\n");
            foreach (var column in entity.Columns)
            {
                sb.Append("        public ").Append(column.ClrType).Append(column.Nullable ? "?" : "").Append(' ').Append(column.PropertyName).Append(" { get; set; }");
                if (!column.Nullable && !column.IsValueType) sb.Append(" = null!;");
                sb.Append('\n');
            }
            foreach (var relation in entity.Relations)
                sb.Append("        /// <summary>The row of the table ").Append(ConnectionCodeContext.Doc(relation.Principal.Table.Name)).Append(" this row points to.</summary>\n")
                    .Append("        public ").Append(relation.Principal.ClassName).Append("? ").Append(relation.ReferenceName).Append(" { get; set; }\n");
            foreach (var (dependent, relation) in model.SelectMany(e => e.Relations.Select(r => (Dependent: e, Relation: r))).Where(x => x.Relation.Principal == entity))
                sb.Append("        /// <summary>The rows of the table ").Append(ConnectionCodeContext.Doc(dependent.Table.Name)).Append(" that point to this row.</summary>\n")
                    .Append("        public global::System.Collections.Generic.ICollection<").Append(dependent.ClassName).Append("> ").Append(relation.CollectionName)
                    .Append(" { get; set; } = new global::System.Collections.Generic.List<").Append(dependent.ClassName).Append(">();\n");
            sb.Append("    }\n");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Key(List<SqlProperty> properties) => properties.Count == 1
        ? "x => x." + properties[0].PropertyName
        : "x => new { " + string.Join(", ", properties.Select(p => "x." + p.PropertyName)) + " }";
}
