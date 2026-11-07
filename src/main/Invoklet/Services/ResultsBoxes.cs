using System.Text;
using System.Text.RegularExpressions;

namespace Invoklet.Services;

/// <summary>
/// The boxes <c>Dump</c> writes around its tables and objects, turned into what the results panel shows: a title strip with an arrow, the
/// title and a "…" for each, and the content of those that are open. The written page keeps everything, so folding loses nothing and the
/// CSV of a table holds what is folded in it. The panel has no scripts: a click is a link it reports, and the page is laid out again.
/// </summary>
internal static partial class ResultsBoxes
{
    private const string TogglePrefix = "invoklet-node:";
    private const string TablePrefix = "invoklet-box:";

    [GeneratedRegex("""<div class="dp-box" data-dp-node="([^"]+)"( data-dp-collapsed="1")?>""")]
    private static partial Regex BoxOpen();

    [GeneratedRegex("""\G<div class="dp-(?:count|type)">([^<]*)</div>""")]
    private static partial Regex OwnTitle();

    private const string PlainDumpBlock = """<div class="dp-dump-plain">""";
    private const string TableStart = """<table class="dp-table"><thead>""";

    [GeneratedRegex("""<div class="dp-dump"><div class="dp-label dp-label-generated">[^<]*</div>""")]
    private static partial Regex GeneratedDump();

    [GeneratedRegex("""<div class="dp-label dp-label-generated">[^<]*</div>""")]
    private static partial Regex GeneratedLabel();

    public static bool AnyIn(string html) => html.Contains("class=\"dp-box\"", StringComparison.Ordinal);

    /// <summary>The box a link folds or opens, or null for any other link.</summary>
    public static string? ToggleOf(string link) => link.StartsWith(TogglePrefix, StringComparison.Ordinal) ? link[TogglePrefix.Length..] : null;

    /// <summary>The box whose table a link's menu is for, or null for any other link.</summary>
    public static string? TableLinkOf(string link) => link.StartsWith(TablePrefix, StringComparison.Ordinal) ? link[TablePrefix.Length..] : null;

    /// <summary>The link the Tab key goes to from <paramref name="current"/>, the first or last from none; null past either end, where the key leaves the panel.
    /// Only what a table of the results offers takes part.</summary>
    public static string? NextLink(IEnumerable<string> links, string? current, bool forward)
    {
        var own = links.Where(l => ToggleOf(l) is not null || TableLinkOf(l) is not null || ResultsTables.TableOf(l) is not null).ToList();
        if (own.Count == 0) return null;
        if (current is null || own.IndexOf(current) is var at && at < 0) return forward ? own[0] : own[^1];
        var next = at + (forward ? 1 : -1);
        return next >= 0 && next < own.Count ? own[next] : null;
    }

    /// <summary>The page to show. <paramref name="toggled"/> holds the boxes the user turned from how they were written. The title of a table is its
    /// first row, as wide as the table. <paramref name="generatedLabels"/> says whether a label made from a script's source text shows; one the script gave
    /// always does. A dump that loses its label loses the accent bar beside it too. An outermost box with no title has no strip: it is open, with its "…" in the header of its table.</summary>
    public static string Show(string html, IReadOnlySet<string> toggled, bool generatedLabels)
    {
        var page = new StringBuilder(html.Length + 256);
        Append(page, html, 0, html.Length, toggled, nested: false);
        if (generatedLabels) return page.ToString();
        // The accent bar beside a dump goes with its generated heading; a dump with a heading of its own, or with none, keeps it.
        return GeneratedLabel().Replace(GeneratedDump().Replace(page.ToString(), PlainDumpBlock), "");
    }

    private static void Append(StringBuilder page, string html, int start, int end, IReadOnlySet<string> toggled, bool nested)
    {
        var position = start;
        for (var open = BoxOpen().Match(html, start); open.Success && open.Index < end; open = BoxOpen().Match(html, position))
        {
            var close = CloseOf(html, open.Index + open.Length);
            // Markup of the script's own that does not balance: left as it is, and nothing after it is read as a box.
            if (close < 0 || close >= end) break;

            page.Append(html, position, open.Index - position);
            position = close + "</div>".Length;

            var bodyStart = open.Index + open.Length;
            var own = OwnTitle().Match(html, bodyStart);
            if (own.Success) bodyStart += own.Length;
            var id = open.Groups[1].Value;

            if (!nested && !own.Success)
            {
                var content = new StringBuilder();
                Append(content, html, bodyStart, close, toggled, nested: true);
                page.Append("<div class=\"dp-box\">").Append(ResultsTables.WithMenus(content.ToString(), _ => TablePrefix + id)).Append("</div>");
                continue;
            }

            var folded = open.Groups[2].Success != toggled.Contains(id);
            var title = own.Success ? own.Groups[1].Value : "&nbsp;";
            var hasTable = string.CompareOrdinal(html, bodyStart, TableStart, 0, TableStart.Length) == 0;
            var strip = Strip(id, title, folded, hasTable);

            // With no other rows the panel has no width to give a cell that spans columns, or a table of 100% inside it, and draws nothing:
            // the title alone is one plain cell as wide as its text.
            page.Append("<div class=\"dp-box\"><table class=\"dp-table dp-boxed\">").Append(hasTable && !folded ? "<thead>" : "<tbody>")
                .Append("<tr><td class=\"dp-boxstrip\" colspan=\"").Append(hasTable && !folded ? ColumnsOf(html, bodyStart + TableStart.Length) : 1).Append("\">")
                .Append(strip).Append("</td></tr>");
            if (hasTable && !folded)
            {
                Append(page, html, bodyStart + TableStart.Length, close, toggled, nested: true);
            }
            else
            {
                page.Append("</tbody></table>");
                if (!folded) Append(page, html, bodyStart, close, toggled, nested: true);
            }
            page.Append("</div>");
        }
        page.Append(html, position, end - position);
    }

    private static string Strip(string id, string title, bool folded, bool hasTable) =>
        $"""<table class="{(folded ? "dp-boxtitle" : "dp-boxtitle dp-boxtitle-open")}"><tr><td class="dp-boxarrow"><a class="dp-boxtoggle" href="{TogglePrefix}{id}" title="{(folded ? "Expand" : "Collapse")}">{(folded ? "&#9656;" : "&#9662;")}</a></td><td class="dp-boxtext">{title}</td>"""
        + (hasTable ? $"""<td class="dp-menubtn"><a class="dp-tablemenu" href="{TablePrefix}{id}" title="Table actions">&nbsp;&hellip;&nbsp;</a></td>""" : "")
        + "</tr></table>";

    // The cells of the header row that follows.
    private static int ColumnsOf(string html, int start)
    {
        var end = html.IndexOf("</tr>", start, StringComparison.Ordinal);
        var columns = 0;
        for (var i = html.IndexOf("<th", start, StringComparison.Ordinal); i >= 0 && i < end; i = html.IndexOf("<th", i + 3, StringComparison.Ordinal))
            if (html[i + 3] is ' ' or '>') columns++;
        return Math.Max(1, columns);
    }

    /// <summary>The table of a box with what is folded in it, for its CSV; null when the box is gone or holds no table.</summary>
    public static ResultsTable? TableOf(string html, string id)
    {
        for (var open = BoxOpen().Match(html); open.Success; open = open.NextMatch())
        {
            if (open.Groups[1].Value != id) continue;
            var start = open.Index + open.Length;
            var close = CloseOf(html, start);
            if (close < 0) return null;
            var tables = ResultsTables.Parse(html[start..close]);
            return tables.Count > 0 ? tables[0] : null;
        }
        return null;
    }

    // Where the div opened just before start ends, or -1.
    private static int CloseOf(string html, int start)
    {
        var depth = 1;
        for (var i = html.IndexOf("<", start, StringComparison.Ordinal); i >= 0; i = html.IndexOf("<", i + 1, StringComparison.Ordinal))
        {
            if (string.CompareOrdinal(html, i, "</div>", 0, 6) == 0)
            {
                if (--depth == 0) return i;
            }
            else if (string.CompareOrdinal(html, i, "<div", 0, 4) == 0 && i + 4 < html.Length && html[i + 4] is ' ' or '>')
            {
                depth++;
            }
        }
        return -1;
    }
}
