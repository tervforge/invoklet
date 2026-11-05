using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Invoklet.UserControls;
using Invoklet.Windows.Models;

namespace Invoklet.Windows;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow(PreferencesWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
        AddPageSearchKeys(viewModel);
    }

    public PreferencesWindow() => InitializeComponent();

    // Escape closes it, as it does every dialog; settings apply as they change, so nothing is lost. A control that takes Escape itself keeps it.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        Close();
    }

    // What each page shows (its Tag is the node ID) becomes what the search finds the node by: the settings' names and
    // descriptions, their tooltips, the choices of their lists and the values in their fields. Text written in the page is
    // highlighted where it matches; text that comes from a binding is searched as it is at the time, but not highlighted,
    // as the highlight would stop following it. Settings listed from data (the key bindings, the system settings) are added
    // by the view model and highlighted by their templates.
    private void AddPageSearchKeys(PreferencesWindowViewModel viewModel)
    {
        foreach (var page in Pages.Children.OfType<Control>())
        {
            if (page.Tag is not string nodeId) continue;
            var keys = new List<string>();
            foreach (var control in page.GetLogicalDescendants().OfType<Control>().ToList())
            {
                if (ToolTip.GetTip(control) is string tip) keys.Add(tip);
                switch (control)
                {
                    case ToggleButton { Content: string text } toggle:
                        var block = new TextBlock();
                        toggle.Content = block;
                        Highlight(block, text);
                        break;
                    case TextBlock { Text.Length: > 0 } label when BindingOperations.GetBindingExpressionBase(label, TextBlock.TextProperty) is null:
                        Highlight(label, label.Text);
                        break;
                    case TextBlock label:
                        viewModel.AddSearchKeys(nodeId, () => [label.Text]);
                        break;
                    case ComboBox list:
                        viewModel.AddSearchKeys(nodeId, () => list.Items.Select(ShownText));
                        break;
                    case TextBox field:
                        viewModel.AddSearchKeys(nodeId, () => [field.Text]);
                        break;
                }
            }
            viewModel.AddSearchKeys(nodeId, keys);

            void Highlight(TextBlock block, string text)
            {
                keys.Add(text);
                SearchHighlight.SetText(block, text);
                block.Bind(SearchHighlight.QueryProperty, new Binding(nameof(PreferencesWindowViewModel.FilterText)) { Source = viewModel });
            }
        }
    }

    // A list's choice as the list shows it: a theme by its name, anything else by its text.
    private static string? ShownText(object? item) =>
        item?.GetType().GetProperty("Name")?.GetValue(item) as string ?? item?.ToString();
}
