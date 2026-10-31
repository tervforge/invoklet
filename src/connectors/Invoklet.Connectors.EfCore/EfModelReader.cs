using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

namespace Invoklet.Connectors.EfCore;

/// <summary>
/// Reads a database the way <c>dotnet ef dbcontext scaffold</c> does: the provider's own reverse engineering makes the model (types, keys, defaults,
/// generated columns, precision, the names of classes, sets and navigation properties), and this class turns it into <see cref="EfEntity"/>.
/// The code itself is written by <see cref="EfContextCode"/>: EF Core's writers are T4 templates, which Invoklet does not ship.
/// </summary>
internal static class EfModelReader
{
    public static List<EfEntity> Read(string connectionString, string? schema, Action<DbContextOptionsBuilder, string> useProvider,
        Action<IServiceCollection> addProviderServices)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDesignTimeServices();
        var options = new DbContextOptionsBuilder();
        useProvider(options, connectionString);
        using var context = new DbContext(options.Options);
        services.AddDbContextDesignTimeServices(context);
        addProviderServices(services);
        using var provider = services.BuildServiceProvider();

        var database = provider.GetRequiredService<IDatabaseModelFactory>()
            .Create(connectionString, new DatabaseModelFactoryOptions(schemas: schema is null ? [] : [schema]));
        var model = provider.GetRequiredService<IScaffoldingModelFactory>().Create(database, new ModelReverseEngineerOptions());
        return From(model);
    }

    internal static List<EfEntity> From(IModel model)
    {
        var entityTypes = model.GetEntityTypes().OrderBy(e => e.GetSchema() ?? e.GetViewSchema(), StringComparer.Ordinal)
            .ThenBy(e => e.GetViewName() ?? e.GetTableName(), StringComparer.Ordinal).ToList();
        var result = new List<EfEntity>();
        foreach (var entityType in entityTypes)
        {
            var isView = entityType.GetViewName() is not null;
            var key = entityType.FindPrimaryKey();
            var columns = new List<EfColumn>();
            foreach (var property in entityType.GetProperties())
            {
                var clr = property.ClrType;
                var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
                columns.Add(new EfColumn(
                    property.GetColumnName(), property.Name, property.GetColumnType(), CsType(underlying), underlying.IsValueType, !property.IsNullable,
                    key is null ? 0 : key.Properties.ToList().IndexOf(property) + 1,
                    property.ValueGenerated switch
                    {
                        ValueGenerated.OnAdd => "OnAdd",
                        ValueGenerated.OnAddOrUpdate or ValueGenerated.OnUpdate => "OnAddOrUpdate",
                        _ => "Never",
                    },
                    // The type is spelled out only where the provider would not choose it from the C# type.
                    property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value as string,
                    property.GetDefaultValueSql(), property.GetComputedColumnSql()));
            }
            var foreignKeys = entityType.GetForeignKeys().Select(fk => new EfForeignKey(
                fk.PrincipalEntityType.GetSchema() ?? fk.PrincipalEntityType.GetViewSchema() ?? "",
                fk.PrincipalEntityType.GetTableName() ?? fk.PrincipalEntityType.GetViewName() ?? "",
                [.. fk.Properties.Select(p => p.GetColumnName())], [.. fk.PrincipalKey.Properties.Select(p => p.GetColumnName())],
                fk.PrincipalEntityType.Name, [.. fk.Properties.Select(p => p.Name)], [.. fk.PrincipalKey.Properties.Select(p => p.Name)],
                fk.DependentToPrincipal?.Name, fk.PrincipalToDependent?.Name)).ToList();
            result.Add(new EfEntity(entityType.GetSchema() ?? entityType.GetViewSchema() ?? "", entityType.GetViewName() ?? entityType.GetTableName() ?? entityType.Name,
                isView, entityType.Name, entityType.GetDbSetName() ?? entityType.Name + "s", columns)
            { ForeignKeys = foreignKeys });
        }
        return result;
    }

    // A type as C# writes it.
    internal static string CsType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(object)) return "object";
        if (type.IsArray) return CsType(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var name = type.GetGenericTypeDefinition().FullName!;
            return "global::" + name[..name.IndexOf('`')].Replace('+', '.') + "<" + string.Join(", ", type.GetGenericArguments().Select(CsType)) + ">";
        }
        return "global::" + type.FullName!.Replace('+', '.');
    }
}
