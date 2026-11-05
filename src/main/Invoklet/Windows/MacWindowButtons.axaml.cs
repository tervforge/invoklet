using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace Invoklet.Windows;

public partial class MacWindowButtons : UserControl
{
    private static readonly IBrush[] ActiveColors =
        [Brush.Parse("#FF5F57"), Brush.Parse("#FEBC2E"), Brush.Parse("#28C840")];
    private static readonly IBrush InactiveColor = Brush.Parse("#8E8E93");

    private Window? _window;
    private bool _windowActive = true;

    public MacWindowButtons()
    {
        AvaloniaXamlLoader.Load(this);
        // Option held as the green button is pressed; a tunnel handler, as the button takes the press itself.
        this.FindControl<Button>("ZoomButton")!.AddHandler(PointerPressedEvent,
            (_, e) => _optionHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt), RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = this.FindAncestorOfType<Window>();
        if (_window is null) return;
        _window.Activated += Window_Activated;
        _window.Deactivated += Window_Deactivated;
        // A window that can't be resized can't be minimized or zoomed either, as with AppKit's dialogs.
        this.FindControl<Button>("MinimizeButton")!.IsEnabled = _window.CanResize;
        this.FindControl<Button>("ZoomButton")!.IsEnabled = _window.CanResize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is null) return;
        _window.Activated -= Window_Activated;
        _window.Deactivated -= Window_Deactivated;
        _window = null;
    }

    private void Window_Activated(object? sender, EventArgs e) => SetWindowActive(true);

    private void Window_Deactivated(object? sender, EventArgs e) => SetWindowActive(false);

    private void SetWindowActive(bool active)
    {
        _windowActive = active;
        if (!active) SetGlyphsVisible(false);
        SetDotColors(active);
    }

    private void SetDotColors(bool highlighted)
    {
        string[] dots = ["CloseDot", "MinimizeDot", "ZoomDot"];
        for (var i = 0; i < dots.Length; i++)
            this.FindControl<Ellipse>(dots[i])!.Fill = highlighted ? ActiveColors[i] : InactiveColor;
    }

    private void Button_PointerEntered(object? sender, PointerEventArgs e)
    {
        SetDotColors(true);
        SetGlyphsVisible(true);
    }

    private void Buttons_PointerExited(object? sender, PointerEventArgs e)
    {
        SetDotColors(_windowActive);
        if (this.FindControl<StackPanel>("Buttons")?.IsPointerOver != true)
            SetGlyphsVisible(false);
    }

    private void SetGlyphsVisible(bool visible)
    {
        foreach (var button in this.FindControl<StackPanel>("Buttons")!.Children.OfType<Button>())
            if (button.Content is Grid grid && grid.Children.Count > 1)
                ((AvaloniaPath)grid.Children[1]).IsVisible = visible;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => _window?.Close();

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (_window is { } window) window.WindowState = WindowState.Minimized;
    }

    private void Zoom_Click(object? sender, RoutedEventArgs e)
    {
        if (_window is { } window) window.WindowState = GreenButtonState(window.WindowState, _optionHeld);
        _optionHeld = false;
    }

    private bool _optionHeld;

    /// <summary>
    /// What the green button does, as on every Mac window: it enters full screen, and leaves it; with Option it zooms the window to fill
    /// the screen beside the Dock and the menu bar, or gives it back its size.
    /// </summary>
    internal static WindowState GreenButtonState(WindowState current, bool option) => current switch
    {
        WindowState.FullScreen => WindowState.Normal,
        _ when option => current == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized,
        _ => WindowState.FullScreen,
    };

    /// <summary>A double click on the title bar zooms, as macOS does by default; a full-screen window keeps its state.</summary>
    internal static WindowState TitleDoubleClickState(WindowState current) => current switch
    {
        WindowState.FullScreen => WindowState.FullScreen,
        WindowState.Maximized => WindowState.Normal,
        _ => WindowState.Maximized,
    };
}
