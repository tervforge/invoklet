using System.Text;

namespace Invoklet.Connectors.Sql;

/// <summary>A table as the script sees it: a set on the context and a class for its rows.</summary>
internal sealed record SqlEntity(SqlTable Table, string SetName, string ClassName, List<SqlProperty> Columns, List<string> Key)
{
    // EF Core can only read a table without a primary key (and a view).
    public bool Keyless => Key.Count == 0;

    public List<SqlRelation> Relations { get; } = [];
}

internal sealed record SqlProperty(SqlColumn Column, string PropertyName, string ClrType, bool IsValueType, bool Nullable);

/// <summary>A foreign key as <c>order.Customer</c> on the table that holds it and <c>customer.Orders</c> on the table it points to.</summary>
internal sealed record SqlRelation(SqlEntity Principal, string ReferenceName, string CollectionName, List<SqlProperty> ForeignKey, List<SqlProperty> PrincipalKey);

/// <summary>
/// Turns the server's names into C#: <c>order_items</c> is the set <c>OrderItems</c> of <c>OrderItem</c> rows, a column is a property in PascalCase.
/// A table outside the default schema is prefixed with its schema when the connection reads more than one (<c>SalesOrders</c>).
/// </summary>
internal static class SqlModel
{
    // Members of DbContext (and of object) a set or a row class can't be named.
    private static readonly string[] ContextMembers =
    [
        "Add", "AddAsync", "AddRange", "AddRangeAsync", "Attach", "AttachRange", "ChangeTracker", "ConfigureConventions",
        "ConnectionString", "ContextId", "Database", "Dispose", "DisposeAsync", "Entry", "Equals", "Finalize", "Find",
        "FindAsync", "FromExpression", "GetHashCode", "GetType", "MemberwiseClone", "Model", "OnConfiguring",
        "OnModelCreating", "Remove", "RemoveRange", "SaveChanges", "SaveChangesAsync", "SaveChangesFailed", "SavedChanges",
        "SavingChanges", "Set", "ToString", "Update", "UpdateRange",
    ];

    private static readonly string[] ObjectMembers = ["Equals", "GetHashCode", "GetType", "MemberwiseClone", "ToString"];

    public static List<SqlEntity> Build(IReadOnlyList<SqlTable> tables, string clientClassName, string defaultSchema)
    {
        var taken = new HashSet<string>(ContextMembers, StringComparer.Ordinal) { clientClassName };
        var manySchemas = tables.Select(t => t.Schema).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var entities = new List<SqlEntity>();
        var memberSets = new List<HashSet<string>>();
        foreach (var table in tables)
        {
            var prefix = manySchemas && !table.Schema.Equals(defaultSchema, StringComparison.OrdinalIgnoreCase) ? Pascal(table.Schema, "Schema") : "";
            var name = prefix + Pascal(table.Name, "Table");
            var setName = Unique(name, taken, "Table");
            var singular = prefix + Singular(Pascal(table.Name, "Table"));
            var className = Unique(singular == setName ? singular + "Row" : singular, taken, "Row");

            var members = new HashSet<string>(ObjectMembers, StringComparer.Ordinal) { className };
            var columns = new List<SqlProperty>();
            foreach (var column in table.Columns)
                columns.Add(new SqlProperty(column, Unique(Pascal(column.Name, "Column"), members, "Value"), column.ClrType, column.IsValueType, !column.NotNull));
            var key = table.IsView ? [] : columns.Where(c => c.Column.PrimaryKey > 0).OrderBy(c => c.Column.PrimaryKey).Select(c => c.PropertyName).ToList();
            entities.Add(new SqlEntity(table, setName, className, columns, key));
            memberSets.Add(members);
        }
        AddRelations(entities, memberSets);
        return entities;
    }

    // A foreign key becomes navigation properties when EF Core can model it: both tables have a key and each pair of columns has one type.
    private static void AddRelations(List<SqlEntity> entities, List<HashSet<string>> memberSets)
    {
        var byName = entities.Select((e, i) => (e, i)).ToDictionary(x => (x.e.Table.Schema.ToLowerInvariant(), x.e.Table.Name.ToLowerInvariant()), x => x);
        foreach (var (dependent, dependentIndex) in entities.Select((e, i) => (e, i)))
        {
            if (dependent.Keyless) continue;
            foreach (var foreignKey in dependent.Table.ForeignKeys)
            {
                if (!byName.TryGetValue((foreignKey.ReferencedSchema.ToLowerInvariant(), foreignKey.Table.ToLowerInvariant()), out var found) || found.e.Keyless) continue;
                var (principal, principalIndex) = found;
                var from = foreignKey.Columns.Select(c => dependent.Columns.Find(p => p.Column.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
                var to = foreignKey.ReferencedColumns.Count == 0
                    ? principal.Key.Select(k => principal.Columns.Find(p => p.PropertyName == k)).ToList()
                    : foreignKey.ReferencedColumns.Select(c => principal.Columns.Find(p => p.Column.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
                if (from.Count == 0 || from.Count != to.Count || from.Contains(null) || to.Contains(null)) continue;
                if (from.Zip(to).Any(pair => pair.First!.ClrType != pair.Second!.ClrType)) continue;

                // order.customer_id is order.Customer; a key of several columns, or one not named like that, is named after the table.
                var reference = from is [{ } single] && single.PropertyName.Length > 2 && single.PropertyName.EndsWith("Id", StringComparison.Ordinal)
                    ? single.PropertyName[..^2] : principal.ClassName;
                reference = Unique(reference, memberSets[dependentIndex], "Ref");
                var collection = Unique(dependent.SetName, memberSets[principalIndex], "By" + reference);
                dependent.Relations.Add(new SqlRelation(principal, reference, collection, from!, to!));
            }
        }
    }

    // order_items, "Order Items" and orderItems are OrderItems; a name with nothing to keep is the fallback.
    public static string Pascal(string name, string fallback)
    {
        var sb = new StringBuilder(name.Length);
        var upper = true;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        if (sb.Length == 0) return fallback;
        if (char.IsDigit(sb[0])) sb.Insert(0, fallback);
        return sb.ToString();
    }

    // English plurals as table names usually have them; anything else stays as it is.
    public static string Singular(string name) =>
        name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 3 ? name[..^3] + "y"
        : name.EndsWith("sses", StringComparison.Ordinal) || name.EndsWith("xes", StringComparison.Ordinal)
          || name.EndsWith("ches", StringComparison.Ordinal) || name.EndsWith("shes", StringComparison.Ordinal) ? name[..^2]
        : name.EndsWith('s') && !name.EndsWith("ss", StringComparison.Ordinal) && !name.EndsWith("us", StringComparison.Ordinal)
          && !name.EndsWith("is", StringComparison.Ordinal) && name.Length > 1 ? name[..^1]
        : name;

    private static string Unique(string name, HashSet<string> taken, string suffix)
    {
        if (taken.Add(name)) return name;
        var withSuffix = name + suffix;
        var candidate = withSuffix;
        for (var i = 2; !taken.Add(candidate); i++) candidate = withSuffix + i;
        return candidate;
    }
}
