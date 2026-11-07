using Avalonia.Controls;
using Avalonia.Media;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Services;
using Invoklet.UserControls.Healpers;

namespace Invoklet.UserControls;

/// <summary>
/// Answers the editor's parameter-info requests from the language service: one row per overload, its signature with the active
/// parameter marked, and under it the descriptions of the method and of that parameter. Builds controls, so it runs on the UI thread.
/// </summary>
internal sealed class EditorParameterInfo(IIntelliSenseService intelliSense, CodeTextEditor editor, Func<bool> isDark)
{
    public async Task ProvideAsync(OverloadRequestedEventArgs e)
    {
        var result = await intelliSense.GetSignatureHelpAsync(editor.Text, e.CaretOffset, e.Trigger, e.CancellationToken);
        if (result is null) return;
        var dark = isDark();
        // Within the editor: most of its width, never wider than a comfortable reading line, never so narrow a parameter has no room.
        var maxWidth = Math.Clamp(editor.TextArea.TextView.Bounds.Width * 0.8, 320, 900);
        // The editor's font is monospace; a character is about 0.6 of its size wide.
        var charWidth = editor.FontSize * 0.6;
        foreach (var item in result.Items)
        {
            var activeParameter = item.Parameters.ElementAtOrDefault(item.ActiveParameterIndex);
            var content = OverloadDescription.Of(item.Summary, activeParameter);
            e.Overloads.Add((
                DisplaySegmentRenderer.BuildParameterInfoSignature(item.Signature, dark, maxWidth, charWidth),
                content is null ? null : new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = maxWidth }));
        }
    }
}
