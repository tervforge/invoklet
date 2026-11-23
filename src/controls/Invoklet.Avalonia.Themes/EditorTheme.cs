using System.Collections.Concurrent;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace Invoklet.Avalonia.Themes;

public sealed record EditorTheme(string Name, IRawTheme RawTheme, bool IsDark)
{
    private static readonly ConcurrentDictionary<ThemeName, IRawTheme> BuiltInThemes = new();

    public string Id { get; init; } = Name;
    public string? Source { get; init; }
    public string DisplayName => Source is null ? Name : $"{Name} ({Source})";

    public EditorTheme(string name, ThemeName theme, bool isDark)
        : this(name, GetBuiltIn(theme), isDark) => Id = $"textmate.{theme.ToString().ToLowerInvariant()}";

    public static IRawTheme GetBuiltIn(ThemeName theme) =>
        BuiltInThemes.GetOrAdd(theme, static name => new RegistryOptions(name).GetDefaultTheme());
}
