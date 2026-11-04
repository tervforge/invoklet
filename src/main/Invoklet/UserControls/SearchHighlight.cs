using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Invoklet.UserControls;

/// <summary>
/// Shows a text in a <see cref="TextBlock"/> with every occurrence of a search query highlighted, case-insensitively:
/// set <c>Text</c> instead of <see cref="TextBlock.Text"/>, and <c>Query</c> to what is searched for.
/// </summary>
public static class SearchHighlight
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(SearchHighlight));

    public static readonly AttachedProperty<string?> QueryProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Query", typeof(SearchHighlight));

    // Whether the block already redraws itself when it joins a window or the theme changes: the highlight brush comes from the
    // theme's resources, which a block in a template that is not shown yet can't find.
    private static readonly AttachedProperty<bool> HookedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("Hooked", typeof(SearchHighlight));

    static SearchHighlight()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((block, _) => Apply(block));
        QueryProperty.Changed.AddClassHandler<TextBlock>((block, _) => Apply(block));
    }

    public static string? GetText(TextBlock block) => block.GetValue(TextProperty);
    public static void SetText(TextBlock block, string? value) => block.SetValue(TextProperty, value);
    public static string? GetQuery(TextBlock block) => block.GetValue(QueryProperty);
    public static void SetQuery(TextBlock block, string? value) => block.SetValue(QueryProperty, value);

    /// <summary>The text cut where the query occurs, each piece marked whether it is an occurrence.</summary>
    public static IReadOnlyList<(string Text, bool IsMatch)> Split(string text, string? query)
    {
        var pieces = new List<(string, bool)>();
        var trimmed = query?.Trim();
        var start = 0;
        if (!string.IsNullOrEmpty(trimmed))
        {
            for (var at = text.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(trimmed, start, StringComparison.OrdinalIgnoreCase))
            {
                if (at > start) pieces.Add((text[start..at], false));
                pieces.Add((text.Substring(at, trimmed.Length), true));
                start = at + trimmed.Length;
            }
        }
        if (start < text.Length || pieces.Count == 0) pieces.Add((text[start..], false));
        return pieces;
    }

    private static void Apply(TextBlock block)
    {
        if (!block.GetValue(HookedProperty))
        {
            block.SetValue(HookedProperty, true);
            block.AttachedToVisualTree += (_, _) => Apply(block);
            block.ActualThemeVariantChanged += (_, _) => Apply(block);
        }
        var inlines = new InlineCollection();
        foreach (var (piece, isMatch) in Split(GetText(block) ?? "", GetQuery(block)))
        {
            var run = new Run(piece);
            if (isMatch)
            {
                run.FontWeight = FontWeight.SemiBold;
                if (block.TryFindResource("Invoklet.Dump.HighlightBrush", block.ActualThemeVariant, out var back) && back is IBrush background)
                    run.Background = background;
                if (block.TryFindResource("Invoklet.Dump.HighlightForegroundBrush", block.ActualThemeVariant, out var fore) && fore is IBrush foreground)
                    run.Foreground = foreground;
            }
            inlines.Add(run);
        }
        block.Inlines = inlines;
    }
}
