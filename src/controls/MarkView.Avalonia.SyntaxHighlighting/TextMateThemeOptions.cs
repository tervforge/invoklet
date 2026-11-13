using TextMateSharp.Grammars;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace MarkView.Avalonia.SyntaxHighlighting;

internal sealed class TextMateThemeOptions(RegistryOptions grammars, IRawTheme theme) : IRegistryOptions
{
    public IRawGrammar GetGrammar(string scopeName) => grammars.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName) => grammars.GetInjections(scopeName);

    public IRawTheme GetTheme(string scopeName) => grammars.GetTheme(scopeName);

    public IRawTheme GetDefaultTheme() => theme;
}
