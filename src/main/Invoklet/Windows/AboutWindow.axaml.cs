using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Invoklet.Services.Updates;

namespace Invoklet.Windows;

public partial class AboutWindow : Window
{
    private readonly IUpdateCheckService? _updateCheck;
    private readonly CancellationTokenSource _updateCheckCancellation = new();
    private readonly Version _currentVersion;
    private Uri? _releaseUri;

    public AboutWindow(IUpdateCheckService? updateCheck = null)
    {
        _updateCheck = updateCheck;
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        _currentVersion = assembly.GetName().Version ?? new Version();
        var authors = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        VersionBlock.Text = $"Version {version}";
        // The whole version, for when the line is cut short.
        ToolTip.SetTip(VersionBlock, VersionBlock.Text);
        AutomationProperties.SetHelpText(VersionBlock, VersionBlock.Text);
        AuthorsBlock.Text = string.IsNullOrEmpty(authors) ? "" : $"By {authors}";
        AuthorsBlock.IsVisible = !string.IsNullOrEmpty(authors);
        CopyrightBlock.Text = copyright ?? "";
        CopyrightBlock.IsVisible = !string.IsNullOrEmpty(copyright);
        Opened += CheckForUpdates;
        Closed += (_, _) =>
        {
            _updateCheckCancellation.Cancel();
            _updateCheckCancellation.Dispose();
        };
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        if (_updateCheck is null) return;
        var ct = _updateCheckCancellation.Token;
        UpdateStatus.Text = "Checking for updates…";
        try
        {
            var result = await _updateCheck.CheckNowAsync(_currentVersion, ct);
            if (!result.Checked)
            {
                UpdateStatus.Text = "Could not check for updates.";
                return;
            }
            if (result.Available is not { } available)
            {
                UpdateStatus.Text = "Invoklet is up to date.";
                return;
            }
            _releaseUri = available.ReleaseUri;
            UpdateStatus.Text = $"Invoklet {available.Version.ToString(3)} is available.";
            ViewReleaseButton.IsVisible = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async void ViewReleaseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_releaseUri is { } uri) await Launcher.LaunchUriAsync(uri);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();
}
