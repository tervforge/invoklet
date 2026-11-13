using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Styling;
using MarkView.Avalonia.SyntaxHighlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace Invoklet.Services;

internal static partial class CodeOutputHighlighter
{
    internal const string CopyPrefix = "invoklet-copy:";

    private static readonly TextMateHighlighter Dark = new(ThemeName.DarkPlus);
    private static readonly TextMateHighlighter Light = new(ThemeName.LightPlus);
    private static readonly ConditionalWeakTable<IRawTheme, TextMateHighlighter> Themes = new();

    public static string Highlight(string html, ThemeVariant? theme = null, IRawTheme? editorTheme = null)
    {
        if (!html.Contains("class=\"dp-code\"", StringComparison.Ordinal)) return html;

        var highlighter = editorTheme is null
            ? UsesLightPalette(theme) ? Light : Dark
            : Themes.GetValue(editorTheme, static raw => new TextMateHighlighter(raw));
        lock (highlighter)
            return CodeBlock().Replace(html, match => RenderBlock(match, highlighter));
    }

    private static bool UsesLightPalette(ThemeVariant? theme)
    {
        for (var variant = theme; variant is not null; variant = variant.InheritVariant)
            if (variant == ThemeVariant.Light) return true;

        return false;
    }

    private static string RenderBlock(Match match, TextMateHighlighter highlighter)
    {
        var language = WebUtility.HtmlDecode(match.Groups[1].Value);
        var source = WebUtility.HtmlDecode(match.Groups[2].Value);
        var output = new StringBuilder(source.Length + 64);
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var i = 0;
        foreach (var tokens in highlighter.HighlightLines(lines, language))
        {
            if (i > 0) output.Append('\n');
            if (tokens is null)
            {
                output.Append(WebUtility.HtmlEncode(lines[i]));
            }
            else
            {
                foreach (var token in tokens)
                {
                    var text = WebUtility.HtmlEncode(token.Text);
                    if (token.Foreground is ISolidColorBrush brush)
                    {
                        var color = brush.Color;
                        output.Append($"<span style=\"color:#{color.R:X2}{color.G:X2}{color.B:X2}\">")
                            .Append(text).Append("</span>");
                    }
                    else
                    {
                        output.Append(text);
                    }
                }
            }
            i++;
        }

        var copy = WebUtility.UrlEncode(Convert.ToBase64String(Encoding.UTF8.GetBytes(source)));
        return $"<div class=\"dp-code-shell\"><a class=\"dp-code-copy\" href=\"{CopyPrefix}{copy}#{match.Index}\" title=\"Copy code\" aria-label=\"Copy code\">⧉</a>"
             + $"<pre class=\"dp-code\" data-language=\"{WebUtility.HtmlEncode(language)}\">{output}</pre></div>";
    }

    [GeneratedRegex("<pre class=\"dp-code\" data-language=\"([^\"]*)\">([\\s\\S]*?)</pre>")]
    private static partial Regex CodeBlock();
}
