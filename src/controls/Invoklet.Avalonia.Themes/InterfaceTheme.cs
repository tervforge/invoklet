using Avalonia.Styling;

namespace Invoklet.Avalonia.Themes;

public sealed record InterfaceTheme(string Name, ThemeVariant Variant, bool IsDark)
{
    public string Id => (string)Variant.Key;
    public string? Source { get; init; }
    public string DisplayName => Source is null ? Name : $"{Name} ({Source})";
    public ResolvedTheme? Definition { get; internal init; }
}
