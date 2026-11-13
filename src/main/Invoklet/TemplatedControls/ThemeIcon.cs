using Avalonia;
using Avalonia.Controls;

namespace Invoklet.TemplatedControls;

/// <summary>Resolves a data-bound icon key in the icon's resource scope.</summary>
public static class ThemeIcon
{
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<PathIcon, string?>("Key", typeof(ThemeIcon));

    private static readonly AttachedProperty<IDisposable?> BindingProperty =
        AvaloniaProperty.RegisterAttached<PathIcon, IDisposable?>("Binding", typeof(ThemeIcon));

    static ThemeIcon()
    {
        KeyProperty.Changed.AddClassHandler<PathIcon>(OnKeyChanged);
    }

    public static string? GetKey(PathIcon icon) => icon.GetValue(KeyProperty);

    public static void SetKey(PathIcon icon, string? value) => icon.SetValue(KeyProperty, value);

    private static void OnKeyChanged(PathIcon icon, AvaloniaPropertyChangedEventArgs change)
    {
        icon.GetValue(BindingProperty)?.Dispose();
        icon.SetValue(BindingProperty, change.NewValue is string key
            ? icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(key))
            : null);
    }
}
