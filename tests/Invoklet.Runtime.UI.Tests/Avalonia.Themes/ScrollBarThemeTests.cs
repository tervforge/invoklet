using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.Avalonia.Themes;

// The one scroll bar of the application, as the theme gives it to every ScrollViewer.
public sealed class ScrollBarThemeTests
{
    private static (Window Window, ScrollViewer Viewer, ScrollBar Bar) Show(double contentHeight = 2000)
    {
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 2000, Height = contentHeight },
        };
        var window = new Window { Width = 400, Height = 300, Content = viewer };
        window.Styles.Add(TestInterfaceThemes.CreateStyle());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == global::Avalonia.Layout.Orientation.Vertical);
        return (window, viewer, bar);
    }

    [AvaloniaFact]
    public void ScrollBar_InTheTheme_HasAThumbAndNoArrows()
    {
        // Arrange & Act
        var (window, _, bar) = Show();

        // Assert
        Assert.Equal(12, bar.Bounds.Width);
        Assert.Single(bar.GetVisualDescendants().OfType<Thumb>());
        Assert.Equal(["PART_PageDownButton", "PART_PageUpButton"], bar.GetVisualDescendants().OfType<RepeatButton>().Select(b => b.Name).Order());
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(100000)]
    public void Thumb_AnyContent_KeepsItsShortLength(double contentHeight)
    {
        // Arrange & Act
        var (window, _, bar) = Show(contentHeight);

        // Assert
        Assert.Equal(76, bar.GetVisualDescendants().OfType<Thumb>().Single().Bounds.Height);
        window.Close();
    }

    // A view shorter than the thumb: the thumb fits the bar and the view still scrolls by the bar's value.
    [AvaloniaFact]
    public void Thumb_AViewShorterThanTheThumb_StaysInsideTheBar()
    {
        // Arrange
        var (window, viewer, bar) = Show();
        window.Height = 50;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();

        // Act
        bar.Value = 300;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.True(thumb.Bounds.Bottom <= bar.Bounds.Height + 0.5, $"thumb ends at {thumb.Bounds.Bottom}, bar at {bar.Bounds.Height}");
        Assert.Equal(300, viewer.Offset.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Thumb_NothingToScroll_IsHidden()
    {
        // Arrange & Act
        var (window, _, bar) = Show(contentHeight: 10);

        // Assert
        Assert.False(bar.GetVisualDescendants().OfType<Thumb>().Single().IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Thumb_AtEachEndOfTheContent_LiesAtThatEndOfTheBar()
    {
        // Arrange
        var (window, viewer, bar) = Show();
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var top = thumb.Bounds.Top;

        // Act
        viewer.Offset = new Vector(0, viewer.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Equal(0, top, 1);
        Assert.Equal(bar.Bounds.Height, thumb.Bounds.Bottom, 1);
        window.Close();
    }

    // The whole way of the thumb is the whole way of the content.
    [AvaloniaFact]
    public void Thumb_Dragged_ScrollsInProportion()
    {
        // Arrange
        var (window, viewer, bar) = Show();
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var way = bar.Bounds.Height - thumb.Bounds.Height;
        var range = viewer.Extent.Height - viewer.Viewport.Height;

        // Act
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, way / 2));
        window.MouseUp(start + new Vector(0, way / 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Equal(range / 2, viewer.Offset.Y, 0);
        window.Close();
    }

    [AvaloniaFact]
    public void ScrollBar_TheViewerScrolled_MovesItsThumb()
    {
        // Arrange
        var (window, viewer, bar) = Show();
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var before = thumb.Bounds.Top;

        // Act
        viewer.Offset = new Vector(0, 1000);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.True(thumb.Bounds.Top > before);
        window.Close();
    }

    [AvaloniaFact]
    public void ScrollBar_MovedByItself_ScrollsTheViewer()
    {
        // Arrange
        var (window, viewer, bar) = Show();

        // Act
        bar.Value = 500;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Assert
        Assert.Equal(500, viewer.Offset.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void ScrollViewer_TheCornerBetweenTheBars_IsNotFilled()
    {
        // Arrange & Act
        var (window, viewer, _) = Show();

        // Assert
        var corner = viewer.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_ScrollBarsSeparator");
        Assert.False(corner.IsVisible);
        window.Close();
    }
}
