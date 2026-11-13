using System.Collections.Immutable;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace Invoklet.Avalonia.Themes;

public sealed record ThemeDefinitionResult(ThemeDefinition? Definition, IReadOnlyList<ThemeDiagnostic> Diagnostics);

/// <summary>Reads theme data without loading markup or opening any asset referenced by it.</summary>
public sealed class ThemeDefinitionReader(IReadOnlyDictionary<string, ThemeKey>? catalog = null)
{
    private readonly IReadOnlyDictionary<string, ThemeKey> _catalog = catalog ?? ThemeKeyCatalog.All;

    internal ThemeDefinition ReadBuiltIn(string asset)
    {
        using var stream = typeof(ThemeDefinitionReader).Assembly.GetManifestResourceStream(asset)
            ?? throw new InvalidOperationException($"The built-in theme resource '{asset}' is missing.");
        using var text = new StreamReader(stream);
        var result = Read(text.ReadToEnd());
        if (result.Definition is not { } theme || result.Diagnostics.Count != 0)
            throw new InvalidOperationException($"The built-in theme resource '{asset}' is invalid. "
                + string.Join(" ", result.Diagnostics.Select(item => $"{item.Field}: {item.Message}")));
        return theme;
    }

    public ThemeDefinitionResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadDefinition(document.RootElement)
                : InvalidDocument("A theme must be a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return InvalidDocument("The theme is not valid JSON.");
        }
    }

    private ThemeDefinitionResult ReadDefinition(JsonElement root)
    {
        var diagnostics = new List<ThemeDiagnostic>();
        var id = ReadString(root, "id");
        var themeId = id ?? "?";
        var name = ReadString(root, "name");
        if (id is null) diagnostics.Add(InvalidField(themeId, "id", "A theme id is required."));
        if (name is null) diagnostics.Add(InvalidField(themeId, "name", "A theme name is required."));

        ThemeBase? themeBase = null;
        if (root.TryGetProperty("base", out var baseValue))
        {
            themeBase = ReadString(baseValue) switch
            {
                "dark" => ThemeBase.Dark,
                "light" => ThemeBase.Light,
                _ => null,
            };
            if (themeBase is null) diagnostics.Add(InvalidField(themeId, "base", "Expected 'dark' or 'light'."));
        }

        var extends = ReadString(root, "extends");
        if (root.TryGetProperty("extends", out var parent) && parent.ValueKind != JsonValueKind.Null && extends is null)
            diagnostics.Add(InvalidField(themeId, "extends", "Expected a parent theme id or null."));

        if (diagnostics.Count > 0) return new ThemeDefinitionResult(null, diagnostics.AsReadOnly());

        var resources = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        ReadResources(root, id!, "colors", resources, diagnostics);
        ReadResources(root, id!, "metrics", resources, diagnostics);
        ReadResources(root, id!, "icons", resources, diagnostics);
        var editor = ReadEditor(root, id!, diagnostics);
        return new ThemeDefinitionResult(new ThemeDefinition(id!, name!, themeBase, extends, resources.ToImmutable(), editor),
            diagnostics.AsReadOnly());
    }

    private void ReadResources(JsonElement root, string id, string section, ImmutableDictionary<string, object>.Builder resources,
        List<ThemeDiagnostic> diagnostics)
    {
        if (!TryReadObject(root, id, section, diagnostics, out var values)) return;
        foreach (var value in values.EnumerateObject())
        {
            var field = $"{section}.{value.Name}";
            if (!_catalog.TryGetValue(value.Name, out var key))
            {
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticCode.UnknownKey, id, field, "Unknown theme key.", IsError: false));
                continue;
            }

            if (section != SectionFor(key.Type))
            {
                diagnostics.Add(InvalidField(id, field, $"The key belongs in '{SectionFor(key.Type)}'."));
                continue;
            }

            if (ReadValue(key.Type, value.Value) is { } resource)
                resources.Add(key.Name, resource);
            else
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticCode.InvalidValue, id, field, $"Expected a {key.Type} value."));
        }
    }

    private static string SectionFor(ThemeKeyType type) => type switch
    {
        ThemeKeyType.Color => "colors",
        ThemeKeyType.Geometry => "icons",
        _ => "metrics",
    };

    private static object? ReadValue(ThemeKeyType type, JsonElement value) => type switch
    {
        ThemeKeyType.Color => ReadString(value) is { } color && Color.TryParse(color, out var parsed) ? parsed : null,
        ThemeKeyType.Double => ReadNumber(value),
        ThemeKeyType.Thickness => ReadComponents(value) switch
        {
            [var uniform] => new Thickness(uniform),
            [var horizontal, var vertical] => new Thickness(horizontal, vertical),
            [var left, var top, var right, var bottom] => new Thickness(left, top, right, bottom),
            _ => null,
        },
        ThemeKeyType.CornerRadius => ReadComponents(value) switch
        {
            [>= 0 and var uniform] => new CornerRadius(uniform),
            [>= 0 and var topLeft, >= 0 and var topRight, >= 0 and var bottomRight, >= 0 and var bottomLeft] =>
                new CornerRadius(topLeft, topRight, bottomRight, bottomLeft),
            _ => null,
        },
        ThemeKeyType.Geometry => ReadGeometry(value),
        _ => null,
    };

    private static double? ReadNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    private static double[] ReadComponents(JsonElement value)
    {
        if (ReadNumber(value) is { } uniform) return [uniform];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is 0 or > 4) return [];

        var result = new double[value.GetArrayLength()];
        var index = 0;
        foreach (var component in value.EnumerateArray())
        {
            if (ReadNumber(component) is not { } number) return [];
            result[index++] = number;
        }

        return result;
    }

    private static PathGeometry? ReadGeometry(JsonElement value)
    {
        if (ReadString(value) is not { } path) return null;
        try
        {
            return PathGeometry.Parse(path);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static ThemeEditorDefinition? ReadEditor(JsonElement root, string id, List<ThemeDiagnostic> diagnostics)
    {
        if (!TryReadObject(root, id, "editor", diagnostics, out var editor)) return null;
        var editorDefault = ReadString(editor, "default");
        if (editorDefault is null && editor.TryGetProperty("default", out _))
            diagnostics.Add(InvalidField(id, "editor.default", "Expected an editor theme id."));

        var themes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (TryReadObject(editor, id, "themes", diagnostics, out var entries, field: "editor.themes"))
        {
            foreach (var entry in entries.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || ReadString(entry.Value) is not { } path)
                    diagnostics.Add(InvalidField(id, $"editor.themes.{entry.Name}", "Expected an editor theme id and an asset path."));
                else
                    themes.Add(entry.Name, path);
            }
        }

        return new ThemeEditorDefinition(editorDefault, themes.ToImmutable());
    }

    private static bool TryReadObject(JsonElement root, string id, string property, List<ThemeDiagnostic> diagnostics,
        out JsonElement value, string? field = null)
    {
        if (!root.TryGetProperty(property, out value)) return false;
        if (value.ValueKind == JsonValueKind.Object) return true;
        diagnostics.Add(InvalidField(id, field ?? property, "Expected an object."));
        return false;
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? ReadString(value) : null;

    private static string? ReadString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static ThemeDefinitionResult InvalidDocument(string message) =>
        new(null, [new ThemeDiagnostic(ThemeDiagnosticCode.InvalidDocument, "?", "", message)]);

    private static ThemeDiagnostic InvalidField(string id, string field, string message) =>
        new(ThemeDiagnosticCode.InvalidField, id, field, message);
}
