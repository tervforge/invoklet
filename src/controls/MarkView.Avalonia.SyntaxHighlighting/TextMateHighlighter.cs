// Copyright (c) Nicolas Musset
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Media;
using Avalonia.Media.Immutable;
using MarkView.Avalonia.Extensions;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace MarkView.Avalonia.SyntaxHighlighting;

/// <summary>
/// Tokenises source-code lines using TextMate grammars and maps token
/// scopes to <see cref="IBrush"/> colours via the chosen theme.
/// </summary>
public sealed class TextMateHighlighter : ICodeHighlighter
{
    private readonly RegistryOptions _options;
    private readonly Registry _registry;
    private readonly Theme _theme;

    // Grammar cache: language id → grammar (null = unsupported)
    private readonly Dictionary<string, IGrammar?> _grammarCache = new(StringComparer.OrdinalIgnoreCase);

    // Brush cache: theme colour hex string → brush (avoids per-token allocation)
    private readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);

    public TextMateHighlighter(ThemeName theme = ThemeName.DarkPlus)
        : this(new RegistryOptions(theme), null) { }

    public TextMateHighlighter(IRawTheme theme)
        : this(new RegistryOptions(ThemeName.DarkPlus), theme) { }

    private TextMateHighlighter(RegistryOptions options, IRawTheme? theme)
    {
        _options = options;
        _registry = new Registry(theme is null ? options : new TextMateThemeOptions(options, theme));
        _theme = _registry.GetTheme();
    }

    /// <inheritdoc/>
    public IReadOnlyList<(string Text, IBrush? Foreground)>? Highlight(ReadOnlyMemory<char> line, string? language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        var grammar = GetGrammar(language);
        if (grammar == null)
            return null;

        IStateStack? state = null;
        return TokenizeLine(grammar, line, ref state);
    }

    /// <summary>Highlights a complete block, carrying grammar state between its lines.</summary>
    public IEnumerable<IReadOnlyList<(string Text, IBrush? Foreground)>?> HighlightLines(
        IEnumerable<string> lines, string language)
    {
        var grammar = GetGrammar(language);
        IStateStack? state = null;
        foreach (var line in lines)
            yield return grammar is null ? null : TokenizeLine(grammar, line.AsMemory(), ref state);
    }

    private IGrammar? GetGrammar(string language)
    {
        if (_grammarCache.TryGetValue(language, out var cached))
            return cached;

        var scopeName = _options.GetScopeByLanguageId(language);
        // Stryker disable once block : LoadGrammar(null) returns null and the next line caches it, so result and caching are identical; only one extra LoadGrammar call differs
        if (scopeName == null)
        {
            _grammarCache[language] = null;
            return null;
        }

        var grammar = _registry.LoadGrammar(scopeName);
        _grammarCache[language] = grammar;
        return grammar;
    }

    private IReadOnlyList<(string Text, IBrush? Foreground)> TokenizeLine(
        IGrammar grammar, ReadOnlyMemory<char> line, ref IStateStack? state)
    {
        // Implicit ReadOnlyMemory<char> → LineText conversion; TextMateSharp 2.x uses
        // ArrayPool internally for the required trailing '\n', avoiding a string allocation.
        var result = grammar.TokenizeLine(line, state, TimeSpan.MaxValue);
        state = result.RuleStack;
        var tokens = new List<(string Text, IBrush? Foreground)>(result.Tokens.Length);

        for (int i = 0; i < result.Tokens.Length; i++)
        {
            var token = result.Tokens[i];
            int start = token.StartIndex;
            int end = i + 1 < result.Tokens.Length
                ? result.Tokens[i + 1].StartIndex
                : line.Length;
            end = Math.Min(end, line.Length);

            if (start >= end)
                continue;

            var text = line.Span[start..end].ToString();
            IBrush? brush = null;

            var rules = _theme.Match(token.Scopes);
            if (rules.Count > 0 && rules[0].foreground != 0)
            {
                var hex = _theme.GetColor(rules[0].foreground);
                if (!string.IsNullOrEmpty(hex))
                {
                    if (!_brushCache.TryGetValue(hex, out brush))
                        _brushCache[hex] = brush = new ImmutableSolidColorBrush(Color.Parse(hex));
                }
            }

            tokens.Add((text, brush));
        }

        return tokens;
    }
}
