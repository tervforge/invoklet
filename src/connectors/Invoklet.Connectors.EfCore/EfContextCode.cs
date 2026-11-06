using System.Text;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;

namespace Invoklet.Connectors.EfCore;

/// <summary>
/// The <c>DbContext</c> of a database: a set per table and view, each row a nested class, the keys and the navigation properties. It depends
/// only on what was read from the database, so it is a file of the schema, compiled once into an assembly of its own and kept; a connection's
/// class in the script derives from it and only says how to open the database.
/// </summary>
internal static class EfContextCode
{
    /// <summary>The name of the generated context class.</summary>
    public const string ContextClass = "Database";

    // Members of DbContext (and of object) a row class can't be named.
    private static readonly string[] ContextMembers =
    [
        "Add", "AddAsync", "AddRange", "AddRangeAsync", "Attach", "AttachRange", "ChangeTracker", "ConfigureConventions",
        "ConnectionString", "ContextId", "Database", "Dispose", "DisposeAsync", "Entry", "Equals", "Finalize", "Find",
        "FindAsync", "FromExpression", "GetHashCode", "GetType", "MemberwiseClone", "Model", "OnConfiguring",
        "OnModelCreating", "Remove", "RemoveRange", "SaveChanges", "SaveChangesAsync", "SaveChangesFailed", "SavedChanges",
        "SavingChanges", "Set", "ToString", "Update", "UpdateRange",
    ];

    /// <summary>The connection's class in the script: the database's context, opened with the connection's string.</summary>
    /// <param name="contextType">The full name of the context of <see cref="GenerateContext"/>.</param>
    /// <param name="constructorStatements">C# statements, indented for the constructor, that assign <c>ConnectionString</c>.</param>
    public static string GenerateClient(MdRunConnection connection, ConnectionCodeContext context, string kind, string constructorStatements,
        string contextType) =>
        new StringBuilder()
            .Append("/// <summary>The ").Append(kind).Append(" connection '").Append(ConnectionCodeContext.Doc(connection.Name))
            .Append("': an Entity Framework Core context with a set per table.</summary>\n")
            .Append("public sealed class ").Append(context.ClassName).Append(" : ").Append(contextType).Append("\n{\n")
            .Append("    internal ").Append(context.ClassName).Append("()\n    {\n")
            .Append(constructorStatements)
            .Append("    }\n}")
            .ToString();

    /// <summary>The context of the database, in <paramref name="namespaceName"/>.</summary>
    /// <param name="useCall">The EF Core provider's extension that takes the options builder and the connection string.</param>
    public static string GenerateContext(string namespaceName, string kind, string useCall, IReadOnlyList<EfEntity> entities)
    {
        const string ef = "global::Microsoft.EntityFrameworkCore";

        // A row class or a set can't take the name of a member of the context or of the context itself.
        var taken = new HashSet<string>(ContextMembers, StringComparer.Ordinal) { ContextClass };
        var classNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var setNames = new Dictionary<EfEntity, string>();
        foreach (var entity in entities) setNames[entity] = Unique(entity.SetName, taken, "Table");
        foreach (var entity in entities) classNames[entity.ClassName] = Unique(entity.ClassName, taken, "Row");

        var sb = new StringBuilder()
            .Append("#nullable enable\n")
            .Append("namespace ").Append(namespaceName).Append(";\n\n")
            .Append("/// <summary>The ").Append(kind).Append(" database: an Entity Framework Core context with a set per table.</summary>\n")
            .Append("public abstract class ").Append(ContextClass).Append(" : ").Append(ef).Append(".DbContext\n{\n")
            .Append("    /// <summary>What the context opens the database with.</summary>\n")
            .Append("    public string ConnectionString { get; protected set; } = \"\";\n\n")
            .Append("    protected override void OnConfiguring(").Append(ef).Append(".DbContextOptionsBuilder optionsBuilder) =>\n")
            .Append("        ").Append(useCall).Append("(optionsBuilder, ConnectionString);\n");

        foreach (var entity in entities)
            sb.Append('\n')
                .Append("    /// <summary>The ").Append(entity.IsView ? "view " : "table ").Append(ConnectionCodeContext.Doc(Qualified(entity)))
                .Append(Key(entity).Count == 0 ? " (read only: it has no primary key)" : "").Append(".</summary>\n")
                .Append("    public ").Append(ef).Append(".DbSet<").Append(classNames[entity.ClassName]).Append("> ").Append(setNames[entity])
                .Append(" => Set<").Append(classNames[entity.ClassName]).Append(">();\n");

        sb.Append('\n')
            .Append("    protected override void OnModelCreating(").Append(ef).Append(".ModelBuilder modelBuilder)\n    {\n");
        foreach (var entity in entities)
        {
            var key = Key(entity);
            sb.Append("        modelBuilder.Entity<").Append(classNames[entity.ClassName]).Append(">(e =>\n        {\n")
                .Append("            ").Append(ef).Append(".RelationalEntityTypeBuilderExtensions.").Append(entity.IsView ? "ToView" : "ToTable")
                .Append("(e, ").Append(ConnectionCodeContext.Literal(entity.Name))
                .Append(entity.Schema.Length == 0 ? "" : ", " + ConnectionCodeContext.Literal(entity.Schema)).Append(");\n");
            if (key.Count == 0) sb.Append("            e.HasNoKey();\n");
            else if (key.Count == 1) sb.Append("            e.HasKey(x => x.").Append(key[0]).Append(");\n");
            else sb.Append("            e.HasKey(x => new { ").Append(string.Join(", ", key.Select(k => "x." + k))).Append(" });\n");
            foreach (var column in entity.Columns)
            {
                sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasColumnName(e.Property(x => x.")
                    .Append(column.Property).Append("), ").Append(ConnectionCodeContext.Literal(column.Name)).Append(");\n");
                if (column.ColumnType is { } columnType)
                    sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasColumnType(e.Property(x => x.")
                        .Append(column.Property).Append("), ").Append(ConnectionCodeContext.Literal(columnType)).Append(");\n");
                if (column.ComputedSql is { } computed)
                    sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasComputedColumnSql(e.Property(x => x.")
                        .Append(column.Property).Append("), ").Append(ConnectionCodeContext.Literal(computed)).Append(");\n");
                else if (column.DefaultSql is { } defaultSql)
                    sb.Append("            ").Append(ef).Append(".RelationalPropertyBuilderExtensions.HasDefaultValueSql(e.Property(x => x.")
                        .Append(column.Property).Append("), ").Append(ConnectionCodeContext.Literal(defaultSql)).Append(");\n");
                if (column.ConcurrencyToken)
                    sb.Append("            e.Property(x => x.").Append(column.Property).Append(").IsConcurrencyToken();\n");
                if (column.Generated == "OnAdd")
                    sb.Append("            e.Property(x => x.").Append(column.Property).Append(").ValueGeneratedOnAdd();\n");
                else if (column.Generated == "OnAddOrUpdate")
                    sb.Append("            e.Property(x => x.").Append(column.Property).Append(").ValueGeneratedOnAddOrUpdate();\n");
            }
            foreach (var foreignKey in entity.ForeignKeys)
            {
                var principal = classNames.GetValueOrDefault(foreignKey.PrincipalClass);
                if (principal is null) continue;
                // One row per row of the other table: a one-to-one relationship, which EF Core wants told which side holds the key.
                var one = foreignKey.IsUnique;
                sb.Append("            e.HasOne").Append(foreignKey.Reference is null ? "<" + principal + ">(" : "(x => x." + foreignKey.Reference).Append(")")
                    .Append(one ? ".WithOne(" : ".WithMany(").Append(foreignKey.Collection is null ? "" : "x => x." + foreignKey.Collection).Append(")")
                    .Append(".HasForeignKey").Append(one ? "<" + classNames[entity.ClassName] + ">" : "").Append("(").Append(Properties(foreignKey.Properties)).Append(")")
                    .Append(".HasPrincipalKey").Append(one ? "<" + principal + ">" : "").Append("(").Append(Properties(foreignKey.PrincipalProperties)).Append(");\n");
            }
            sb.Append("        });\n");
        }
        sb.Append("    }\n");

        foreach (var entity in entities)
        {
            sb.Append('\n')
                .Append("    /// <summary>A row of the ").Append(entity.IsView ? "view " : "table ").Append(ConnectionCodeContext.Doc(Qualified(entity))).Append(".</summary>\n")
                .Append("    public sealed class ").Append(classNames[entity.ClassName]).Append("\n    {\n");
            foreach (var column in entity.Columns)
            {
                sb.Append("        public ").Append(column.ClrType).Append(column.NotNull ? "" : "?").Append(' ').Append(column.Property).Append(" { get; set; }");
                if (column.NotNull && !column.IsValueType) sb.Append(" = null!;");
                sb.Append('\n');
            }
            foreach (var foreignKey in entity.ForeignKeys.Where(f => f.Reference is not null && classNames.ContainsKey(f.PrincipalClass)))
                sb.Append("        /// <summary>The row of the table ").Append(ConnectionCodeContext.Doc(foreignKey.Table)).Append(" this row points to.</summary>\n")
                    .Append("        public ").Append(classNames[foreignKey.PrincipalClass]).Append("? ").Append(foreignKey.Reference).Append(" { get; set; }\n");
            foreach (var (dependent, foreignKey) in entities.SelectMany(e => e.ForeignKeys.Select(f => (Dependent: e, Key: f)))
                         .Where(x => x.Key.Collection is not null && x.Key.PrincipalClass == entity.ClassName))
            {
                if (foreignKey.IsUnique)
                    sb.Append("        /// <summary>The row of the table ").Append(ConnectionCodeContext.Doc(dependent.Name)).Append(" that points to this row, if there is one.</summary>\n")
                        .Append("        public ").Append(classNames[dependent.ClassName]).Append("? ").Append(foreignKey.Collection).Append(" { get; set; }\n");
                else
                    sb.Append("        /// <summary>The rows of the table ").Append(ConnectionCodeContext.Doc(dependent.Name)).Append(" that point to this row.</summary>\n")
                        .Append("        public global::System.Collections.Generic.ICollection<").Append(classNames[dependent.ClassName]).Append("> ").Append(foreignKey.Collection)
                        .Append(" { get; set; } = new global::System.Collections.Generic.List<").Append(classNames[dependent.ClassName]).Append(">();\n");
            }
            sb.Append("    }\n");
        }
        sb.Append("}\n#nullable restore\n");
        return sb.ToString();
    }

    private static string Qualified(EfEntity entity) => entity.Schema.Length == 0 ? entity.Name : entity.Schema + "." + entity.Name;

    // The properties of the primary key, in the order of the key.
    private static List<string> Key(EfEntity entity) => [.. entity.Columns.Where(c => c.PrimaryKey > 0).OrderBy(c => c.PrimaryKey).Select(c => c.Property)];

    private static string Properties(List<string> properties) => properties.Count == 1
        ? "x => x." + properties[0]
        : "x => new { " + string.Join(", ", properties.Select(p => "x." + p)) + " }";

    private static string Unique(string name, HashSet<string> taken, string suffix)
    {
        if (taken.Add(name)) return name;
        var withSuffix = name + suffix;
        var candidate = withSuffix;
        for (var i = 2; !taken.Add(candidate); i++) candidate = withSuffix + i;
        return candidate;
    }
}
