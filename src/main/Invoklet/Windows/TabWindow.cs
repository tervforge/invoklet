using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Invoklet.Services.Preferences;
using Invoklet.UserControls;

namespace Invoklet.Windows;

// A window for one tab moved out of the main window's strip: the tab's body (the editor and the results), no headers or side panels. The
// keys of the tab work here as in the main window. Closing the window, or the tab's Close key, gives the tab back to the main window as it
// is; the window closes by itself if the tab is closed for good (a file deleted, the app quitting).
internal sealed class TabWindow : Window
{
    private readonly EditorTabsControl _strip;
    private readonly IPreferencesService? _prefs;
    private bool _closingConfirmed;

    public TabWindow(EditorTabsControl strip, IPreferencesService? prefs)
    {
        _strip = strip;
        _prefs = prefs;
        Title = "Invoklet";
        Content = strip;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        Activated += (_, _) =>
        {
            strip.WindowActivated();
            _ = strip.CheckFilesChangedOnDiskAsync();
        };
        strip.Emptied += OnEmptied;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            strip.Emptied -= OnEmptied;
            strip.Dispose();
        };
    }

    /// <summary>The name of the script shown, as the title.</summary>
    internal void ShowTitle(string? fileName) => Title = fileName is null ? "Invoklet" : $"{fileName} — Invoklet";

    private void OnEmptied()
    {
        _closingConfirmed = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed) return;
        // The tab goes back as it is: nothing unsaved is asked about, it stays unsaved in the main window.
        _closingConfirmed = true;
        _strip.ReturnToMainWindow();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_prefs is null) return;
        foreach (var (command, action) in _strip.Commands.Bindings)
        {
            if (!_prefs.Keyboard.Matches(e, command)) continue;
            // The tab's Close here gives it back to the main window, as the window's close button does.
            if (command == KeyCommands.CloseTab) Close();
            else action();
            e.Handled = true;
            return;
        }
    }
}
