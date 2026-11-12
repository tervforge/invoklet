using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Invoklet.Windows;
using Microsoft.Extensions.Logging;

namespace Invoklet.Services;

internal sealed class ApplicationRestart(IClassicDesktopStyleApplicationLifetime desktop, ILogger<ApplicationRestart> logger,
    Action<ProcessStartInfo>? launch = null)
{
    private readonly Action<ProcessStartInfo> _launch = launch ?? Start;
    private MainWindow? _restartWindow;

    public async Task RequestAsync()
    {
        if (_restartWindow is not null || desktop.MainWindow is not MainWindow { IsVisible: true } window || !window.ClosingTask.IsCompleted) return;
        _restartWindow = window;
        try
        {
            window.Close();
            await window.ClosingTask;
        }
        finally
        {
            if (window.IsVisible) _restartWindow = null;
        }
    }

    public void StartReplacementAfterExit()
    {
        if (_restartWindow is not { IsVisible: false }) return;
        _restartWindow = null;
        var executable = Environment.ProcessPath;
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { } image
            && Environment.GetEnvironmentVariable("APPDIR") is { } appDirectory
            && string.Equals(executable, Path.Combine(appDirectory, "usr", "bin", "Invoklet"), StringComparison.Ordinal)
            && Path.IsPathFullyQualified(image) && File.Exists(image))
            executable = image;

        if (executable is null)
        {
            logger.LogError("Could not restart Invoklet because its executable path is unavailable.");
            return;
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in desktop.Args ?? []) start.ArgumentList.Add(argument);
        try { _launch(start); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not restart Invoklet.");
        }
    }

    private static void Start(ProcessStartInfo start)
    {
        using var replacement = Process.Start(start);
    }
}
