using System.Net;
using System.Text;

namespace Invoklet.Services;

/// <summary>The rows of one table of the results, as the text its cells show.</summary>
internal sealed record ResultsTable(IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// The tables of the results panel's HTML, as <c>Dump</c> writes it. A table that is not inside another one is numbered from 0 in the order it
/// appears; <see cref="WithMenus"/> puts a link for each in the page the panel shows and <see cref="Parse"/> reads their rows, so the number a click
/// carries is the table to save.
/// </summary>
internal static class ResultsTables
{
    private const string LinkPrefix = "invoklet-table:";

    /// <summary>
    /// The page with a "…" button, a cell of its own at the end of the first row, of each table that is not in another; a click raises
    /// LinkClicked with <see cref="TableOf"/> of its address. A table with no rows has none, and keeps its number. (The panel loses the text of a
    /// cell that an inline link follows, and lays a link out on a line of its own, so the button is not put in a cell with text.)
    /// </summary>
    public static string WithMenus(string html)
    {
        var result = new StringBuilder(html.Length + 128);
        var depth = 0;
        var count = 0;
        var copied = 0;
        var lastCell = -1;
        var lastCellTag = "th";
        var firstRowDone = false;
        for (var i = html.IndexOf('<'); i >= 0; i = html.IndexOf('<', i + 1))
        {
            var close = html.IndexOf('>', i);
            if (close < 0) break;
            var tag = html[(i + 1)..close];
            var closing = tag.StartsWith('/');
            switch (NameOf(tag))
            {
                case "table":
                    if (!closing)
                    {
                        if (depth++ == 0) { lastCell = -1; firstRowDone = false; }
                    }
                    else if (depth > 0 && --depth == 0) count++;
                    break;
                case "td" or "th" when closing && depth == 1 && !firstRowDone:
                    lastCell = close + 1;
                    lastCellTag = NameOf(tag);
                    break;
                case "tr" when closing && depth == 1 && !firstRowDone:
                    firstRowDone = true;
                    if (lastCell >= 0)
                    {
                        result.Append(html, copied, lastCell - copied)
                            .Append('<').Append(lastCellTag).Append(" class=\"dp-").Append(lastCellTag == "th" ? "th" : "td").Append(" dp-menucell\">")
                            .Append("<a class=\"dp-tablemenu\" href=\"").Append(LinkPrefix).Append(count).Append("\" title=\"Table actions\">&nbsp;&hellip;&nbsp;</a></")
                            .Append(lastCellTag).Append('>');
                        copied = lastCell;
                    }
                    break;
            }
        }
        return result.Append(html, copied, html.Length - copied).ToString();
    }

    /// <summary>The number of the table a link of <see cref="WithMenus"/> stands for, or null for any other link.</summary>
    public static int? TableOf(string link) =>
        link.StartsWith(LinkPrefix, StringComparison.Ordinal) && int.TryParse(link.AsSpan(LinkPrefix.Length), out var number) ? number : null;

    /// <summary>Every table that is not inside another, an empty one too, so that the numbers match <see cref="WithMenus"/>. A table inside a cell is the text of that cell.</summary>
    public static IReadOnlyList<ResultsTable> Parse(string html)
    {
        var tables = new List<ResultsTable>();
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var segments = new List<(string? Class, StringBuilder Text)>();
        var depth = 0;
        var inCell = false;
        var rowIsHeader = true;
        var cellIsHeader = false;
        string? skipping = null;

        for (var i = 0; i < html.Length;)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                if (skipping is null && inCell) AddText(WebUtility.HtmlDecode(html[i..next]));
                i = next;
                continue;
            }

            var close = html.IndexOf('>', i);
            if (close < 0) break;
            var tag = html[(i + 1)..close];
            i = close + 1;
            var closing = tag.StartsWith('/');
            var name = NameOf(tag);
            if (skipping is not null)
            {
                if (closing && name == skipping) skipping = null;
                continue;
            }

            switch (name)
            {
                case "style" or "script" or "summary": // the panel's "(click to expand)" is not a result
                    if (!closing) skipping = name;
                    break;
                case "div" when !closing && ClassOf(tag) == "dp-count": // "Person[] — 3 items"
                    skipping = name;
                    break;
                case "table":
                    if (!closing) depth++;
                    else if (depth > 0 && --depth == 0)
                    {
                        tables.Add(new ResultsTable(rows));
                        rows = [];
                    }
                    break;
                case "tr":
                    if (depth == 1)
                    {
                        if (closing) CommitRow();
                        else { row = []; rowIsHeader = true; }
                    }
                    else if (closing && inCell) AddText("; ");
                    break;
                case "td" or "th":
                    if (depth == 1)
                    {
                        if (closing) CommitCell();
                        else { inCell = true; cellIsHeader = name == "th"; segments.Clear(); }
                    }
                    else if (closing && inCell) AddText(" ");
                    break;
                case "span":
                    segments.Add((closing ? null : ClassOf(tag), new StringBuilder()));
                    break;
                case "br" or "div" or "p" or "li" or "details" or "pre":
                    AddText(" ");
                    break;
            }
        }
        return tables;

        void AddText(string text)
        {
            if (segments.Count == 0) segments.Add((null, new StringBuilder()));
            segments[^1].Text.Append(text);
        }

        void CommitRow()
        {
            inCell = false;
            // The panel can repeat the header every so many rows.
            var repeated = rowIsHeader && rows.Count > 0 && row.SequenceEqual(rows[0]);
            if (row.Count > 0 && !repeated) rows.Add(row);
            row = [];
        }

        void CommitCell()
        {
            inCell = false;
            var parts = segments.Where(s => s.Class is not null || s.Text.ToString().Trim().Length > 0).Select(s => (s.Class, Text: s.Text.ToString())).ToList();
            row.Add(parts.Count == 1
                ? parts[0].Class switch
                {
                    "dp-null" => "",
                    "dp-string" => Unquote(parts[0].Text),
                    _ => Tidy(parts[0].Text),
                }
                : Tidy(string.Concat(parts.Select(p => p.Text))));
            rowIsHeader &= cellIsHeader;
            segments.Clear();
        }
    }

    private static string NameOf(string tag)
    {
        var start = tag.StartsWith('/') ? 1 : 0;
        var end = start;
        while (end < tag.Length && char.IsAsciiLetterOrDigit(tag[end])) end++;
        return tag[start..end].ToLowerInvariant();
    }

    private static string? ClassOf(string tag)
    {
        const string Marker = "class=\"";
        var start = tag.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += Marker.Length;
        var end = tag.IndexOf('"', start);
        return end < 0 ? null : tag[start..end];
    }

    // The rows of a table inside a cell end with "; ".
    private static string Tidy(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';', ' ');

    // A string is shown in quotes in the panel; in a cell it is the string.
    private static string Unquote(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
}
