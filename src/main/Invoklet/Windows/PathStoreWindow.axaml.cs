using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Invoklet.Services;
using Invoklet.Windows.Models;

namespace Invoklet.Windows;

// Closes with true after the store was saved, false/null otherwise.
public partial class PathStoreWindow : Window
{
    public PathStoreWindow(PathStoreWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    public PathStoreWindow() => InitializeComponent();

    private PathStoreWindowViewModel? ViewModel => DataContext as PathStoreWindowViewModel;

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Save() == true)
            Close(true);
    }

    // The full path a script would get, not the text as typed.
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedPath?.Resolved is { } path && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(path);
    }

    private bool _askingToSave;

    private async void Cancel_Click(object? sender, RoutedEventArgs e) => await CloseAskingToSaveAsync();

    // The window's own close button asks too. A programmatic Close (Save/Cancel, the app shutting
    // down, tests) never does.
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.IsProgrammatic || ViewModel is not { HasChanges: true }) return;
        e.Cancel = true;
        await CloseAskingToSaveAsync();
    }

    internal async Task CloseAskingToSaveAsync()
    {
        if (_askingToSave) return;
        if (ViewModel is not { HasChanges: true } vm)
        {
            Close(false);
            return;
        }

        _askingToSave = true;
        try
        {
            var choice = await UnsavedChangesDialog
                .WithMessage("The Path Store has unsaved changes. Save them?")
                .ShowDialog<UnsavedChangesResult?>(this);
            switch (choice)
            {
                // A failed save (an invalid entry, a store that changed) keeps the window open.
                case UnsavedChangesResult.Save when vm.Save():
                    Close(true);
                    break;
                case UnsavedChangesResult.DontSave:
                    Close(false);
                    break;
            }
        }
        finally
        {
            _askingToSave = false;
        }
    }
}
