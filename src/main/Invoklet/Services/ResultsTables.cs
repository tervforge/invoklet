using System.Globalization;
using System.Net;
using System.Text;

namespace Invoklet.Services;

internal enum CellKind { Text, Number, Boolean, Header }

internal readonly record struct SheetCell(string Text, CellKind Kind);

internal sealed record SheetData(string Name, IReadOnlyList<IReadOnlyList<SheetCell>> Rows);

/// <summary>
/// Reads the results panel's HTML, as <c>Dump</c> writes it, into sheets: every table that is not inside another becomes one ("Table 1", …), with
/// what it holds in its cells as text; everything shown outside the tables goes, a line to a row, to a last sheet "Output".
/// </summary>
internal static class ResultsTables
{
    // A double holds 15 digits exactly: a longer number (an id) stays text, whole.
    private const int MaxNumberDigits = 15;

    public static IReadOnlyList<SheetData> Parse(string html)
    {
        var sheets = new List<SheetData>();
        var output = new List<IReadOnlyList<SheetCell>>();
        var line = new StringBuilder();
        var rows = new List<IReadOnlyList<SheetCell>>();
        var row = new List<SheetCell>();
        var segments = new List<(string? Class, StringBuilder Text)>();
        var depth = 0;
        var inCell = false;
        var header = false;
        string? skipping = null;
        string? cellClass = null;

        for (var i = 0; i < html.Length;)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                if (skipping is null) AddText(WebUtility.HtmlDecode(html[i..next]));
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
                case "table":
                    if (!closing)
                    {
                        if (depth++ == 0) FlushLine();
                    }
                    else if (depth > 0 && --depth == 0)
                    {
                        if (rows.Count > 0) sheets.Add(new SheetData("Table " + (sheets.Count + 1), rows));
                        rows = [];
                    }
                    break;
                case "tr":
                    if (depth == 1) { if (closing) CommitRow(); else row = []; }
                    else if (closing) AddText("; ");
                    break;
                case "td" or "th":
                    if (depth == 1)
                    {
                        if (closing) CommitCell();
                        else { inCell = true; header = name == "th"; cellClass = ClassOf(tag); segments.Clear(); }
                    }
                    else if (closing) AddText(" ");
                    break;
                case "span":
                    segments.Add((closing ? null : ClassOf(tag), new StringBuilder()));
                    break;
                case "div" when !closing && ClassOf(tag) == "dp-count": // "Person[] — 3 items": the sheet has the rows
                    skipping = name;
                    break;
                case "br" or "div" or "p" or "li" or "details" or "pre":
                    if (depth == 0) FlushLine();
                    else AddText(" ");
                    break;
            }
        }
        FlushLine();
        if (output.Count > 0) sheets.Add(new SheetData("Output", output));
        return sheets;

        void AddText(string text)
        {
            if (depth == 0) line.Append(text);
            else if (inCell)
            {
                if (segments.Count == 0) segments.Add((null, new StringBuilder()));
                segments[^1].Text.Append(text);
            }
        }

        void FlushLine()
        {
            var text = line.ToString().Trim();
            line.Clear();
            if (text.Length > 0) output.Add([new SheetCell(text, CellKind.Text)]);
        }

        void CommitRow()
        {
            inCell = false;
            // The panel can repeat the header every so many rows.
            var repeated = rows.Count > 0 && row.Count == rows[0].Count && row.All(c => c.Kind == CellKind.Header)
                && row.Select(c => c.Text).SequenceEqual(rows[0].Select(c => c.Text));
            if (row.Count > 0 && !repeated) rows.Add(row);
            row = [];
        }

        void CommitCell()
        {
            inCell = false;
            var parts = segments.Where(s => s.Class is not null || s.Text.ToString().Trim().Length > 0).Select(s => (s.Class, Text: s.Text.ToString())).ToList();
            var cell = parts.Count == 1
                ? (parts[0].Class ?? (cellClass?.Contains("dp-number") == true ? "dp-number" : null)) switch
                {
                    "dp-number" when TryNumber(parts[0].Text, out var number) => new SheetCell(number, CellKind.Number),
                    "dp-bool" => new SheetCell(parts[0].Text, CellKind.Boolean),
                    "dp-null" => new SheetCell("", CellKind.Text),
                    "dp-string" => new SheetCell(Unquote(parts[0].Text), CellKind.Text),
                    _ => new SheetCell(Tidy(parts[0].Text), CellKind.Text),
                }
                : new SheetCell(Tidy(string.Concat(parts.Select(p => p.Text))), CellKind.Text);
            row.Add(header ? cell with { Kind = CellKind.Header } : cell);
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

    // The panel writes numbers in the culture of the script; a text neither reads is kept as text (a formatted "1,234.50", "50%").
    private static bool TryNumber(string text, out string number)
    {
        number = text;
        if (text.Count(char.IsAsciiDigit) > MaxNumberDigits) return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return false;
        if (!double.IsFinite(value)) return false;
        number = value.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }
}
