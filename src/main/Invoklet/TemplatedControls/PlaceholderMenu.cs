using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Invoklet.TemplatedControls;

/// <summary>
/// Adds an "Insert" submenu to a TextBox's context menu, with categories for secrets (and later
/// environment variables) that insert a placeholder at the caret. Attach <see cref="SecretNamesProperty"/>
/// to a TextBox in a DataTemplate; when the list is non-empty the context menu gains the submenu.
/// </summary>
public static class PlaceholderMenu
{
    public static readonly AttachedProperty<IReadOnlyList<string>?> SecretNamesProperty =
        AvaloniaProperty.RegisterAttached<TextBox, IReadOnlyList<string>?>("SecretNames", typeof(PlaceholderMenu));

    public static IReadOnlyList<string>? GetSecretNames(TextBox tb) => tb.GetValue(SecretNamesProperty);
    public static void SetSecretNames(TextBox tb, IReadOnlyList<string>? value) => tb.SetValue(SecretNamesProperty, value);

    static PlaceholderMenu()
    {
        SecretNamesProperty.Changed.AddClassHandler<TextBox>(OnSecretNamesChanged);
    }

    private static void OnSecretNamesChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        var names = e.NewValue as IReadOnlyList<string>;
        if (names is null or { Count: 0 })
        {
            textBox.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();
        menu.Opening += (_, _) => FillMenu(menu, textBox);
        textBox.ContextMenu = menu;
    }

    private static void FillMenu(ContextMenu menu, TextBox textBox)
    {
        menu.Items.Clear();

        var cut = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        cut.Click += (_, _) => textBox.Cut();
        cut.IsEnabled = textBox.SelectedText?.Length > 0;

        var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += (_, _) => textBox.Copy();
        copy.IsEnabled = textBox.SelectedText?.Length > 0;

        var paste = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        paste.Click += (_, _) => textBox.Paste();

        var selectAll = new MenuItem { Header = "Select All", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
        selectAll.Click += (_, _) => textBox.SelectAll();
        selectAll.IsEnabled = textBox.Text?.Length > 0;

        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(selectAll);

        var names = GetSecretNames(textBox);
        if (names is { Count: > 0 })
        {
            menu.Items.Add(new Separator());
            var insert = new MenuItem { Header = "Insert" };
            var secretSub = new MenuItem { Header = "Secret" };
            foreach (var name in names)
            {
                var n = name;
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => InsertText(textBox, $"${{secret:{n}}}");
                secretSub.Items.Add(item);
            }
            insert.Items.Add(secretSub);
            menu.Items.Add(insert);
        }
    }

    private static void InsertText(TextBox textBox, string text)
    {
        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        var current = textBox.Text ?? "";
        textBox.Text = current[..start] + text + current[end..];
        textBox.CaretIndex = start + text.Length;
    }
}
