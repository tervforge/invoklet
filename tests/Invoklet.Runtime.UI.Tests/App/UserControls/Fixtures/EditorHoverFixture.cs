using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts.Debugging;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.UserControls;
using Moq;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;

// An editor in a window with one line of C#, the analysis service of the hover, and what the tab would hand it: the paused debugger,
// if any, and the diagnostics of the last analysis. No pauses: the hover asks as soon as the pointer stops.
internal sealed class EditorHoverFixture : IDisposable
{
    public const string Text = "var first = 1;";

    public EditorHoverFixture()
    {
        Editor = new CodeTextEditor { Text = Text, FontSize = 14 };
        Editor.IdentifierChars.UnionWith("_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        Window = new Window { Width = 600, Height = 300, Content = Editor };
        Window.Show();
        Window.UpdateLayout();
        Service.Setup(s => s.GetQuickInfoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IQuickInfoResult>(q => q.Signature == Array.Empty<DisplayTextSegment>() && q.Summary == "the first value"));
    }

    public CodeTextEditor Editor { get; }
    public Window Window { get; }
    public Mock<IIntelliSenseService> Service { get; } = new();
    public IBreakpointHitContext? DebugContext { get; set; }
    public List<IDiagnosticInfo> Diagnostics { get; } = [];

    public EditorHover CreateSut() =>
        new(Editor, Service.Object, NoPauses.Timings, () => DebugContext, () => Diagnostics, () => true);

    /// <summary>A stop of the debugger where "first" is a local holding 1.</summary>
    public static IBreakpointHitContext PausedWithFirst() =>
        Mock.Of<IBreakpointHitContext>(c => c.LocalVariables == new[]
        {
            Mock.Of<IVariableInfo>(v => v.Name == "first" && v.Value == "1" && v.TypeName == "int" && !v.HasChildren),
        });

    public static IDiagnosticInfo DiagnosticOverFirst() =>
        Mock.Of<IDiagnosticInfo>(d => d.StartOffset == 4 && d.EndOffset == 9 && d.Message == "boom" && d.Severity == DiagnosticSeverity.Error);

    /// <summary>The middle of a character of the text, in the window.</summary>
    public Point PointAt(int offset)
    {
        var view = Editor.TextArea.TextView;
        var location = Editor.Document.GetLocation(offset);
        var inView = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.TextMiddle) - view.ScrollOffset;
        return view.TranslatePoint(new Point(inView.X + 2, inView.Y), Window)!.Value;
    }

    public object? Tip => ToolTip.GetTip(Editor.TextArea.TextView);

    /// <summary>The value tip of the debugger, wherever the platform hosts popups.</summary>
    public DebugValueTip? ValueTip =>
        Window.GetVisualDescendants().OfType<DebugValueTip>().FirstOrDefault()
        ?? OverlayLayer.GetOverlayLayer(Window)?.GetVisualDescendants().OfType<DebugValueTip>().FirstOrDefault()
        ?? Window.GetVisualDescendants().OfType<Popup>().Select(p => p.Child).OfType<DebugValueTip>().FirstOrDefault()
        ?? PopupsOf(Window);

    private static DebugValueTip? PopupsOf(Window window) =>
        window.GetLogicalDescendants().OfType<Popup>().Select(p => p.Child).OfType<DebugValueTip>().FirstOrDefault();

    public void Dispose()
    {
        Window.Close();
        Editor.Dispose();
    }
}
