using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Windows;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

public class MacWindowButtonsTests
{
    // Each glyph (×, −, the zoom arrows) sits in the middle of its dot, after layout rounding to whole pixels.
    [AvaloniaFact]
    public void EachGlyph_Should_BeCentredOnItsDot()
    {
        var buttons = new MacWindowButtons();
        var window = new Window { Content = buttons, Width = 200, Height = 60 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var grid in buttons.GetVisualDescendants().OfType<Button>().Select(b => (Grid)b.Content!))
            {
                var dot = grid.Children.OfType<Ellipse>().Single();
                var glyph = grid.Children.OfType<ShapePath>().Single();
                var dotCentre = dot.TranslatePoint(new Point(dot.Bounds.Width / 2, dot.Bounds.Height / 2), window)!.Value;
                var drawn = glyph.Data!.Bounds;
                var glyphCentre = glyph.TranslatePoint(drawn.Center, window)!.Value;

                Assert.Equal(dotCentre.X, glyphCentre.X, 3);
                Assert.Equal(dotCentre.Y, glyphCentre.Y, 3);
            }
        }
        finally { window.Close(); }
    }
}
