using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes.Scrolling;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.Avalonia.Themes;

public sealed class ScrollMarkerStripTests
{
    // A hundred lines of ten pixels: twice the height of the window at first.
    private static readonly ScrollMarkerGeometry Lines = new(100, 1000, 10, line => (line - 1) * 10);

    private static (Window Window, ScrollMarkerStrip Strip) Show(params ScrollMarker[] markers)
    {
        var strip = new ScrollMarkerStrip();
        var host = new Panel { Background = global::Avalonia.Media.Brushes.Transparent };
        host.Children.Add(strip);
        strip.AttachInput(host);
        var window = new Window { Width = 100, Height = 400, Content = host };
        window.Show();
        strip.Update(markers, Lines);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, strip);
    }

    private static Point Middle(ScrollMarkerRow row) => new(50, row.Top + row.Height / 2);

    [AvaloniaFact]
    public void Click_OnAMark_GoesToItsLine()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(50, ScrollMarkerKind.Error));
        var lines = new List<int>();
        strip.LineActivated += lines.Add;

        // Act
        window.MouseDown(Middle(strip.Rows[0]), MouseButton.Left);
        window.MouseUp(Middle(strip.Rows[0]), MouseButton.Left);

        // Assert
        Assert.Equal([50], lines);
        window.Close();
    }

    [AvaloniaFact]
    public void Click_AwayFromAMark_DoesNothing()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(50, ScrollMarkerKind.Error));
        var lines = new List<int>();
        strip.LineActivated += lines.Add;
        var away = new Point(50, strip.Rows[0].Top + 40);

        // Act
        window.MouseDown(away, MouseButton.Left);
        window.MouseUp(away, MouseButton.Left);

        // Assert
        Assert.Empty(lines);
        window.Close();
    }

    [AvaloniaFact]
    public void Move_OverAMarkAndAway_ShowsAndTakesBackItsWords()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(50, ScrollMarkerKind.Warning, "careful"));
        var host = (Control)window.Content!;

        // Act
        window.MouseMove(Middle(strip.Rows[0]));
        var over = ToolTip.GetTip(host);
        window.MouseMove(new Point(50, strip.Rows[0].Top + 40));

        // Assert
        Assert.Equal("Line 50: Warning: careful", over);
        Assert.Null(ToolTip.GetTip(host));
        window.Close();
    }

    [AvaloniaFact]
    public void Keyboard_MovingToAMarkAndEnter_GoesToItsLine()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(10, ScrollMarkerKind.Error), new ScrollMarker(80, ScrollMarkerKind.Warning, "careful"));
        var lines = new List<int>();
        strip.LineActivated += lines.Add;
        strip.Focus(NavigationMethod.Tab);

        // Act
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        // Assert
        Assert.Equal([80], lines);
        Assert.Equal("Line 80: Warning: careful", AutomationProperties.GetName(strip));
        Assert.Equal("Line 80: Warning: careful", ToolTip.GetTip(strip));
        window.Close();
    }

    [AvaloniaFact]
    public void Update_NoMarkers_CannotTakeFocus()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(10, ScrollMarkerKind.Error));

        // Act
        strip.Update([], Lines);

        // Assert
        Assert.False(strip.Focusable);
        Assert.Empty(strip.Rows);
        window.Close();
    }

    [AvaloniaFact]
    public void Resize_OfTheStrip_MovesTheMarks()
    {
        // Arrange
        var (window, strip) = Show(new ScrollMarker(50, ScrollMarkerKind.Error));
        var before = strip.Rows[0].Top;

        // Act
        window.Height = 200;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.True(strip.Rows[0].Top < before);
        window.Close();
    }
}
