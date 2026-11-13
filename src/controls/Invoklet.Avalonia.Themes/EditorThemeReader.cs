using System.Text.Json;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Registry;

namespace Invoklet.Avalonia.Themes;

public sealed record EditorThemeReadResult(EditorTheme Theme, string? Diagnostic);

public sealed class EditorThemeReader
{
    /// <summary>Reads a standalone VS Code theme from an asset already opened by its owner.</summary>
    public EditorThemeReadResult Read(Stream stream, string name, EditorTheme fallback)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        try
        {
            var json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                AllowDuplicateProperties = false,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("include", out _)
                || !root.TryGetProperty("tokenColors", out var tokens) || tokens.ValueKind != JsonValueKind.Array
                || tokens.EnumerateArray().Any(rule => rule.ValueKind != JsonValueKind.Object
                    || !rule.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object))
                return new(fallback, "Expected a standalone VS Code theme with a tokenColors array.");

            using var normalized = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(root));
            using var themeReader = new StreamReader(normalized);
            var theme = ThemeReader.ReadThemeSync(themeReader);
            var registry = new Registry(new RegistryOptions(ThemeName.DarkPlus));
            registry.SetTheme(theme);
            var isDark = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString() switch
                {
                    "dark" or "hcDark" => true,
                    "light" or "hcLight" => false,
                    _ => fallback.IsDark,
                }
                : fallback.IsDark;
            return new(new EditorTheme(name, theme, isDark), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or ArgumentException
            or InvalidOperationException or InvalidCastException or NullReferenceException)
        {
            return new(fallback, "The editor theme could not be read as a VS Code theme.");
        }
    }
}
