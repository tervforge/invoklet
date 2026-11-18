using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    private void RefreshPlugins_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel viewModel) viewModel.PluginsPage.Refresh();
    }

    private void TogglePlugin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel viewModel && sender is Button { DataContext: PluginStatusRow row })
            viewModel.PluginsPage.Toggle(row);
    }

    private async void InstallPlugin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesWindowViewModel { PluginsPage.CanInstall: true } model) return;
        string? path;
        try
        {
            path = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a plugin package",
                FileTypeFilter = [new FilePickerFileType("NuGet plugin packages") { Patterns = ["*.nupkg"] }],
            })).FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            model.PluginsPage.ReportPackageError();
            return;
        }
        if (path is null) return;
        await model.PluginsPage.InstallAsync(path,
            manifest => PluginPackageConfirmationDialog.ForInstall(manifest, path).ShowDialog<bool>(this));
    }

    private async void BrowsePlugins_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel model) await model.PluginsPage.BrowseAsync();
    }

    private async void InstallFeedPlugin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel model && sender is Button { DataContext: PluginFeedRow row })
            await model.PluginsPage.InstallFromFeedAsync(row,
                (manifest, path) => PluginPackageConfirmationDialog.ForInstall(manifest, path).ShowDialog<bool>(this));
    }

    private async void RemovePlugin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel model && sender is Button { DataContext: PluginStatusRow row })
            await model.PluginsPage.RemoveAsync(row, () => new PluginPackageConfirmationDialog("Remove plugin?",
                $"Remove {row.Name}? The change will take effect after restarting Invoklet.", "Remove").ShowDialog<bool>(this));
    }

    private async void RestartForPlugins_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesWindowViewModel { PluginsPage.HasPendingRestart: true } viewModel) return;
        Close();
        await Dispatcher.UIThread.InvokeAsync(viewModel.PluginsPage.RestartAsync, DispatcherPriority.Background);
    }

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
