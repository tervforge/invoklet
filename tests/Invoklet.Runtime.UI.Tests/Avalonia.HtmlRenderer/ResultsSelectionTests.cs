using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Invoklet.Avalonia.HtmlRenderer;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.Avalonia.HtmlRenderer;

// Selecting text in the results with the mouse: from where the button went down, whatever the mouse does next, and from anywhere in the panel.
public sealed class ResultsSelectionTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";

    private static (Window Window, HtmlPanel Panel) Show(string body)
    {
        var panel = new HtmlPanel { Text = $"<html><head><style>body {{ margin:0; font-size:20px; }}</style></head><body>{body}</body></html>" };
        var window = new Window { Width = 800, Height = 400, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, panel);
    }

    private static void Drag(Window window, Point from, params Point[] moves)
    {
        window.MouseDown(from, MouseButton.Left);
        foreach (var move in moves) window.MouseMove(move, RawInputModifiers.LeftMouseButton);
        window.MouseUp(moves[^1], MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Characters(string text) => $"<div style=\"word-break:break-all\">{text}</div>";

    // Where a character begins, from how the panel selects: found by selecting up to it would prove nothing, so it is measured.
    private static double X(HtmlPanel panel, string text, int index)
    {
        panel.SelectAll();
        panel.ClearSelection();
        var measure = new TextBlock { Text = text, FontSize = 20 };
        measure.Measure(Size.Infinity);
        return measure.DesiredSize.Width * index / text.Length;
    }

    [AvaloniaFact]
    public void Drag_TheFirstMoveFarFromThePress_StartsWhereTheButtonWentDown()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);

        // Act
        Drag(window, new Point(step * 2 + step / 4, 10), new Point(step * 6 + step / 4, 10));

        // Assert
        Assert.Equal("cdef", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Drag_FromTheLeftOfALetter_TakesThatLetter()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);

        // Act
        Drag(window, new Point(step / 4, 10), new Point(step / 2, 10), new Point(step * 3 + step / 4, 10));

        // Assert
        Assert.Equal("abc", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Drag_FromTheEmptySpaceBelow_SelectsUpToThePoint()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);

        // Act
        Drag(window, new Point(step * 30, 300), new Point(step * 28, 200), new Point(step * 23 + step / 4, 10));

        // Assert
        Assert.Equal("xyz", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Drag_IntoTheEmptySpaceBelow_SelectsToTheEndOfTheText()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);

        // Act
        Drag(window, new Point(step * 23 + step / 4, 10), new Point(step * 24, 200), new Point(step * 24, 300));

        // Assert
        Assert.Equal("xyz", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Drag_FromAboveTheFirstLine_StartsAtItsBeginning()
    {
        // Arrange
        var (window, panel) = Show($"<div style=\"margin-top:60px;word-break:break-all\">{Alphabet}</div>");
        var step = X(panel, Alphabet, 1);

        // Act
        Drag(window, new Point(step * 2 + step / 4, 10), new Point(step * 4, 30), new Point(step * 6 + step / 4, 70));

        // Assert
        Assert.Equal("abcdef", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Press_OnSelectedText_StartsANewSelection()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);
        Drag(window, new Point(step / 4, 10), new Point(step * 10 + step / 4, 10));

        // Act
        Drag(window, new Point(step * 2 + step / 4, 10), new Point(step * 5 + step / 4, 10));

        // Assert
        Assert.Equal("cde", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void Click_OnSelectedText_TakesTheSelectionAway()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);
        Drag(window, new Point(step / 4, 10), new Point(step * 10 + step / 4, 10));
        var inside = new Point(step * 5, 10);

        // Act
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);

        // Assert
        Assert.True(string.IsNullOrEmpty(panel.SelectedText));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("&quot;Ann Lee&quot;", "Ann Lee")]
    [InlineData("&apos;x&apos;", "x")]
    [InlineData("&quot;say &quot;hi&quot; now&quot;", "say \"hi\" now")]
    public void SelectAll_AStringValue_CopiesItWithoutItsOuterQuotes(string shown, string expected)
    {
        // Arrange
        var (window, panel) = Show($"<div><span class=\"dp-string\">{shown}</span></div>");

        // Act
        panel.SelectAll();

        // Assert
        Assert.Equal(expected, panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectAll_TextThatIsNoStringValue_KeepsItsQuotes()
    {
        // Arrange
        var (window, panel) = Show("<div>&quot;plain&quot;</div>");

        // Act
        panel.SelectAll();

        // Assert
        Assert.Equal("\"plain\"", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectAll_RowsOfATable_HaveTabsBetweenCellsAndNoneAtTheEnd()
    {
        // Arrange
        var (window, panel) = Show("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>");

        // Act
        panel.SelectAll();

        // Assert
        Assert.Equal($"a\tb{Environment.NewLine}c\td", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void NewOutput_AtTheEnd_KeepsTheSelection()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);
        Drag(window, new Point(step * 2 + step / 4, 10), new Point(step * 5 + step / 4, 10));

        // Act
        panel.Text = $"<html><head><style>body {{ margin:0; font-size:20px; }}</style></head><body>{Characters(Alphabet)}<div>more</div></body></html>";
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Equal("cde", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void NewOutput_AfterSelectAll_SelectsItToo()
    {
        // Arrange
        var (window, panel) = Show("<div>one</div>");
        panel.SelectAll();

        // Act
        panel.Text = "<html><body><div>one</div><div>two</div></body></html>";
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Contains("two", panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void OtherOutput_ThanTheSelectedOne_TakesTheSelectionAway()
    {
        // Arrange
        var (window, panel) = Show(Characters(Alphabet));
        var step = X(panel, Alphabet, 1);
        Drag(window, new Point(step * 2 + step / 4, 10), new Point(step * 5 + step / 4, 10));

        // Act
        panel.Text = "<html><body><div>something else</div></body></html>";
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.True(string.IsNullOrEmpty(panel.SelectedText));
        window.Close();
    }

    [AvaloniaFact]
    public void SelectAll_ATokenBrokenAcrossLines_IsOneTokenWhenCopied()
    {
        // Arrange
        var token = string.Concat(Enumerable.Repeat(Alphabet, 12));
        var panel = new HtmlPanel { Text = $"<html><body style=\"margin:0\">{Characters(token)}</body></html>" };
        var window = new Window { Width = 300, Height = 400, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Act
        panel.SelectAll();

        // Assert
        Assert.Equal(token, panel.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectAll_TwoCellsOfStrings_TakesTheQuotesOffEach()
    {
        // Arrange
        var (window, panel) = Show("<table><tr><td><span class=\"dp-string\">&quot;Ann&quot;</span></td><td><span class=\"dp-string\">&quot;Bob&quot;</span></td></tr></table>");

        // Act
        panel.SelectAll();

        // Assert
        Assert.Equal("Ann\tBob", panel.SelectedText);
        window.Close();
    }
}
