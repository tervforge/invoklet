#:project ../src/controls/Invoklet.Avalonia.Themes/Invoklet.Avalonia.Themes.csproj

using Invoklet.Avalonia.Themes;

Console.WriteLine("# Interface theme keys");
Console.WriteLine();
Console.WriteLine("Generated from `ThemeKeyCatalog` by `build/GenerateThemeKeyTable.cs`.");

WriteTable("Palette and focus ring", ThemeKeyCatalog.Palette);
WriteTable("Metrics", ThemeKeyCatalog.Metrics);
WriteTable("Icons", ThemeKeyCatalog.Icons);

static void WriteTable(string title, IEnumerable<ThemeKey> keys)
{
    Console.WriteLine();
    Console.WriteLine($"## {title}");
    Console.WriteLine();
    Console.WriteLine("| Key | Type | Purpose | Alias of |");
    Console.WriteLine("| --- | --- | --- | --- |");
    foreach (var key in keys.OrderBy(key => key.Name, StringComparer.Ordinal))
    {
        var type = char.ToLowerInvariant(key.Type.ToString()[0]) + key.Type.ToString()[1..];
        var alias = key.AliasFor is null ? "" : $"`{key.AliasFor}`";
        Console.WriteLine($"| `{key.Name}` | `{type}` | {key.Description} | {alias} |");
    }
}
