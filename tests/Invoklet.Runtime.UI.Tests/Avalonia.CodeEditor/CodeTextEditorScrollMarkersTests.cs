using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Avalonia.Themes;
using Invoklet.Avalonia.Themes.Scrolling;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.Avalonia.CodeEditor;

public sealed class CodeTextEditorScrollMarkersTests
{
    private static (Window Window, CodeTextEditor Editor, ScrollMarkerStrip Strip) Show(int lines = 100, bool applicationTheme = false)
    {
        var editor = new CodeTextEditor { Text = string.Join("\n", Enumerable.Range(1, lines).Select(i => $"line {i}")) };
        var window = new Window { Width = 600, Height = 400, Content = editor };
        if (applicationTheme) window.Styles.Add(TestInterfaceThemes.CreateStyle());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, editor, editor.GetVisualDescendants().OfType<ScrollMarkerStrip>().Single());
    }

    private static void Close(Window window, CodeTextEditor editor)
    {
        window.Close();
        editor.Dispose();
    }

    [AvaloniaFact]
    public void Update_MarkersOfTheOwner_ShowOnTheStrip()
    {
        // Arrange
        var (window, editor, strip) = Show();

        // Act
        editor.UpdateScrollMarkers([new ScrollMarker(40, ScrollMarkerKind.Error, "boom")]);

        // Assert
        Assert.Equal(40, Assert.Single(strip.Rows).Line);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Update_ABreakpointAndTheCurrentLine_AreAddedByTheEditor()
    {
        // Arrange
        var (window, editor, strip) = Show();

        // Act
        editor.AddBreakpoint(20);
        editor.DebugCurrentLine = 70;

        // Assert
        Assert.Equal([(20, ScrollMarkerKind.Breakpoint), (70, ScrollMarkerKind.Execution)], strip.Rows.Select(r => (r.Line, r.Kind)));
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Update_ABreakpointRemovedAndTheDebugRunOver_LeaveNoMarks()
    {
        // Arrange
        var (window, editor, strip) = Show();
        editor.AddBreakpoint(20);
        editor.DebugCurrentLine = 70;

        // Act
        editor.RemoveBreakpoint(editor.Breakpoints.Single());
        editor.DebugCurrentLine = null;

        // Assert
        Assert.Empty(strip.Rows);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Click_OnAMark_PutsTheCaretOnItsLine()
    {
        // Arrange
        var (window, editor, strip) = Show();
        editor.UpdateScrollMarkers([new ScrollMarker(80, ScrollMarkerKind.Warning)]);
        var mark = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, strip.Rows[0].Top + strip.Rows[0].Height / 2), window)!.Value;

        // Act
        window.MouseDown(mark, MouseButton.Left);
        window.MouseUp(mark, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(80, editor.TextArea.Caret.Line);
        Assert.Equal(1, editor.TextArea.Caret.Column);
        Assert.True(editor.TextArea.IsFocused);
        Assert.True(editor.VerticalOffset > 0);
        Close(window, editor);
    }

    // A script that fits: the mark stands beside its line.
    [AvaloniaFact]
    public void Layout_AShortScript_PutsTheMarkBesideItsLine()
    {
        // Arrange
        var (window, editor, strip) = Show(lines: 7);

        // Act
        editor.UpdateScrollMarkers([new ScrollMarker(3, ScrollMarkerKind.Error)]);

        // Assert
        var row = Assert.Single(strip.Rows);
        var view = editor.TextArea.TextView;
        Assert.Equal(view.GetVisualTopByDocumentLine(3) + view.DefaultLineHeight / 2, row.Top + row.Height / 2, 1);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Layout_TheStrip_LiesBetweenTheTextAndTheScrollBars()
    {
        // Arrange & Act
        var (window, editor, strip) = Show();

        // Assert
        var children = Assert.IsAssignableFrom<Panel>(strip.Parent).Children;
        var own = children.IndexOf(strip);
        Assert.True(children.IndexOf(children.OfType<ScrollContentPresenter>().Single()) < own);
        Assert.All(children.OfType<ScrollBar>(), bar => Assert.True(children.IndexOf(bar) > own));
        Close(window, editor);
    }

    // The thumb lies over the mark of the first line: a press there is a drag, not a jump.
    [AvaloniaFact]
    public void Click_OnTheThumbOverAMark_LeavesTheCaret()
    {
        // Arrange
        var (window, editor, strip) = Show(lines: 500, applicationTheme: true);
        editor.UpdateScrollMarkers([new ScrollMarker(5, ScrollMarkerKind.Error)]);
        var thumb = editor.GetVisualDescendants().OfType<Thumb>().Single(t => t.FindAncestorOfType<ScrollBar>()!.Orientation == global::Avalonia.Layout.Orientation.Vertical);
        var row = strip.Rows[0];
        var point = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, row.Top + row.Height / 2), window)!.Value;
        var caret = editor.TextArea.Caret.Line;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Assert
        Assert.True(thumb.Bounds.Height > row.Top + row.Height, "the thumb should lie over the mark");
        Assert.Equal(caret, editor.TextArea.Caret.Line);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Folding_LinesHiddenAbove_MovesTheMarkUp()
    {
        // Arrange
        var (window, editor, strip) = Show(lines: 200);
        editor.UpdateScrollMarkers([new ScrollMarker(150, ScrollMarkerKind.Error)]);
        var before = strip.Rows[0].Top;
        var view = editor.TextArea.TextView;

        // Act
        view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
        var fold = view.CollapseLines(editor.Document.GetLineByNumber(10), editor.Document.GetLineByNumber(100));
        view.EnsureVisualLines();
        editor.UpdateScrollMarkers([new ScrollMarker(150, ScrollMarkerKind.Error)]);

        // Assert
        Assert.NotEqual(before, strip.Rows[0].Top);
        fold.Uncollapse();
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Text_MoreLines_MovesTheMarks()
    {
        // Arrange
        var (window, editor, strip) = Show();
        editor.UpdateScrollMarkers([new ScrollMarker(50, ScrollMarkerKind.Error)]);
        var before = strip.Rows[0].Top;

        // Act
        editor.Document.Insert(editor.Document.TextLength, new string('\n', 100));

        // Assert
        Assert.True(strip.Rows[0].Top < before);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Layout_TheStrip_LeavesTheEditorItsWidth()
    {
        // Arrange
        var (window, editor, strip) = Show();
        var width = editor.TextArea.Bounds.Width;

        // Act
        editor.UpdateScrollMarkers([new ScrollMarker(5, ScrollMarkerKind.Error)]);
        window.UpdateLayout();

        // Assert
        Assert.Equal(width, editor.TextArea.Bounds.Width);
        Assert.Equal(16, strip.Bounds.Width);
        Assert.Equal(editor.Bounds.Width, strip.Bounds.Right);
        Close(window, editor);
    }

    [AvaloniaFact]
    public void Scroll_WithMarks_StillWorksAndLeavesThemInPlace()
    {
        // Arrange
        var (window, editor, strip) = Show();
        editor.UpdateScrollMarkers([new ScrollMarker(50, ScrollMarkerKind.Error)]);
        var top = strip.Rows[0].Top;

        // Act
        editor.ScrollToVerticalOffset(300);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Equal(300, editor.VerticalOffset, 1);
        Assert.Equal(top, strip.Rows[0].Top);
        Close(window, editor);
    }
}
