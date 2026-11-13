using System.Collections.Immutable;

namespace Invoklet.Avalonia.Themes;

public enum ThemeBase
{
    Dark,
    Light,
}

public sealed record ThemeEditorDefinition(string? Default, ImmutableDictionary<string, string> Themes);

/// <summary>The values supplied by one theme. Resources are keyed by their names in <see cref="ThemeKeyCatalog"/>.</summary>
public sealed record ThemeDefinition(string Id, string Name, ThemeBase? Base, string? Extends,
    ImmutableDictionary<string, object> Resources, ThemeEditorDefinition? Editor = null);

public enum ThemeDiagnosticCode
{
    InvalidDocument,
    InvalidField,
    UnknownKey,
    InvalidValue,
    MissingParent,
    InheritanceCycle,
}

public sealed record ThemeDiagnostic(ThemeDiagnosticCode Code, string ThemeId, string Field, string Message, bool IsError = true);
