using AvaloniaEdit.Document;
using Invoklet.Avalonia.Themes.Scrolling;
using Invoklet.Contracts.IntelliSense;

namespace Invoklet.UserControls;

/// <summary>The marks beside the scroll bar for the diagnostics the editor underlines, so that one without a quick fix shows too.</summary>
internal static class DiagnosticScrollMarkers
{
    public static IReadOnlyList<ScrollMarker> Of(TextDocument document, IReadOnlyList<IDiagnosticInfo> diagnostics) =>
        [.. diagnostics.Select(d => new ScrollMarker(
            document.GetLineByOffset(Math.Clamp(d.StartOffset, 0, document.TextLength)).LineNumber,
            d.Severity switch
            {
                DiagnosticSeverity.Error => ScrollMarkerKind.Error,
                DiagnosticSeverity.Warning => ScrollMarkerKind.Warning,
                _ => ScrollMarkerKind.Info,
            },
            d.Message))];
}
