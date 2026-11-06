namespace Invoklet.Avalonia.Themes.Scrolling;

/// <summary>What a mark beside a scroll bar stands for. A later member wins where marks share a row.</summary>
public enum ScrollMarkerKind { Breakpoint, Info, Warning, Error, Execution }

public sealed record ScrollMarker(int Line, ScrollMarkerKind Kind, string? Message = null);

/// <summary>Where the lines of a scrollable view lie in its content: what the marks are placed by.</summary>
public sealed record ScrollMarkerGeometry(int LineCount, double ContentHeight, double LineHeight, Func<int, double> LineTop);

/// <summary>One mark as drawn: the most important of the markers that fall on the same row, and the line a click goes to.</summary>
public sealed record ScrollMarkerRow(double Top, double Height, int Line, ScrollMarkerKind Kind, IReadOnlyList<ScrollMarker> Markers);

/// <summary>Where marks go along a scroll bar. Content that fits lies one to one, so a mark stands beside its line; longer content is
/// scaled to the height. Every mark has one height. Marks that would overlap become one, the most important showing, and so do marks of
/// a kind on lines next to each other, however far apart those lie. A line outside the content is left out.</summary>
public static class ScrollMarkerLayout
{
    public const double RowHeight = 6;

    public static IReadOnlyList<ScrollMarkerRow> Rows(IReadOnlyList<ScrollMarker> markers, ScrollMarkerGeometry geometry, double height)
    {
        if (geometry.LineCount < 1 || height < RowHeight) return [];

        var scale = geometry.ContentHeight > height ? height / geometry.ContentHeight : 1;
        var rows = new List<ScrollMarkerRow>();
        var current = new List<ScrollMarker>();
        var (top, bottom) = (0d, 0d);
        foreach (var marker in markers
                     .Where(m => m.Line >= 1 && m.Line <= geometry.LineCount)
                     .Distinct()
                     .OrderBy(m => m.Line).ThenByDescending(m => m.Kind).ThenBy(m => m.Message, StringComparer.Ordinal))
        {
            var y = Math.Clamp((geometry.LineTop(marker.Line) + geometry.LineHeight / 2) * scale - RowHeight / 2, 0, height - RowHeight);
            if (current.Count > 0 && y >= bottom && !Continues(current, marker))
            {
                rows.Add(RowOf(top, bottom - top, current));
                current = [];
            }
            if (current.Count == 0) top = y;
            current.Add(marker);
            bottom = y + RowHeight;
        }
        if (current.Count > 0) rows.Add(RowOf(top, bottom - top, current));
        return rows;
    }

    // Breakpoints stay dots of their own.
    private static bool Continues(List<ScrollMarker> row, ScrollMarker marker) =>
        marker.Kind != ScrollMarkerKind.Breakpoint && row[^1].Line + 1 == marker.Line && row[^1].Kind == marker.Kind;

    private static ScrollMarkerRow RowOf(double top, double height, List<ScrollMarker> markers)
    {
        var first = markers.MaxBy(m => m.Kind)!; // MaxBy keeps the first of equals: the lowest line
        return new ScrollMarkerRow(top, height, first.Line, first.Kind, markers);
    }

    /// <summary>The words for a mark, in its tooltip and for a screen reader; colour alone does not tell it.</summary>
    public static string Describe(ScrollMarkerRow row)
    {
        var first = row.Markers.First(m => m.Kind == row.Kind && m.Line == row.Line);
        var text = $"Line {row.Line}: {KindName(row.Kind)}";
        if (!string.IsNullOrEmpty(first.Message)) text += $": {first.Message}";
        return row.Markers.Count > 1 ? $"{text} (and {row.Markers.Count - 1} more)" : text;
    }

    private static string KindName(ScrollMarkerKind kind) => kind switch
    {
        ScrollMarkerKind.Error => "Error",
        ScrollMarkerKind.Warning => "Warning",
        ScrollMarkerKind.Info => "Information",
        ScrollMarkerKind.Execution => "Current line",
        _ => "Breakpoint",
    };
}
