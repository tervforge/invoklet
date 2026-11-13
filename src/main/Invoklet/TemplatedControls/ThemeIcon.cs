using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Invoklet.TemplatedControls;

/// <summary>Resolves a data-bound icon key in the icon's resource scope.</summary>
public static class ThemeIcon
{
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Key", typeof(ThemeIcon));

    private static readonly AttachedProperty<IDisposable?> BindingProperty =
        AvaloniaProperty.RegisterAttached<Control, IDisposable?>("Binding", typeof(ThemeIcon));

    static ThemeIcon()
    {
        KeyProperty.Changed.AddClassHandler<PathIcon>((icon, change) => Bind(icon, PathIcon.DataProperty, change.NewValue as string));
        KeyProperty.Changed.AddClassHandler<ShapePath>((icon, change) => Bind(icon, ShapePath.DataProperty, change.NewValue as string));
    }

    public static string? GetKey(Control icon) => icon.GetValue(KeyProperty);

    public static void SetKey(Control icon, string? value) => icon.SetValue(KeyProperty, value);

    private static void Bind(Control icon, StyledProperty<Geometry?> property, string? key)
    {
        icon.GetValue(BindingProperty)?.Dispose();
        icon.SetValue(BindingProperty, key is not null
            ? icon.Bind(property, icon.GetResourceObservable(key))
            : null);
    }
}
