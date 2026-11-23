// Copyright (c) Nicolas Musset
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;
using MarkView.Avalonia.Rendering.Blocks;

using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace MarkView.Avalonia.SyntaxHighlighting;

/// <summary>
/// Wires <see cref="DualThemeTextMateHighlighter"/> and <see cref="TextMateCodeBlockRenderer"/>
/// into an <see cref="AvaloniaRenderer"/>. Two <see cref="TextMateHighlighter"/> instances are
/// created lazily — one for dark themes and one for light — and exposed as an
/// <see cref="IThemeAwareCodeHighlighter"/> so code block renderers can update token colours
/// in-place when the theme changes without rebuilding the entire document.
/// </summary>
public sealed class TextMateExtension : IMarkViewExtension
{
    private readonly Func<TextMateHighlighter> _createDark;
    private readonly Func<TextMateHighlighter> _createLight;
    private TextMateHighlighter? _darkHighlighter;
    private TextMateHighlighter? _lightHighlighter;

    public TextMateExtension(ThemeName darkTheme = ThemeName.DarkPlus, ThemeName lightTheme = ThemeName.LightPlus)
    {
        _createDark = () => new TextMateHighlighter(darkTheme);
        _createLight = () => new TextMateHighlighter(lightTheme);
    }

    public TextMateExtension(IRawTheme theme)
    {
        _createDark = () => new TextMateHighlighter(theme);
        _createLight = GetDark;
    }

    public void Register(AvaloniaRenderer renderer)
    {
        renderer.CodeHighlighter = new DualThemeTextMateHighlighter(GetDark(), GetLight());
        renderer.ReplaceOrAdd<CodeBlockRenderer>(new TextMateCodeBlockRenderer());
    }

    // Stryker disable once assignment : lazily caches the highlighter; without the cache every Register() builds its own instance whose highlighting output is identical, only instance reuse differs
    private TextMateHighlighter GetDark() => _darkHighlighter ??= _createDark();
    // Stryker disable once assignment : lazily caches the highlighter; without the cache every Register() builds its own instance whose highlighting output is identical, only instance reuse differs
    private TextMateHighlighter GetLight() => _lightHighlighter ??= _createLight();
}
