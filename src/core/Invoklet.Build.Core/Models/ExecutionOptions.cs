namespace Invoklet.Build.Core.Models;

public class ExecutionOptions
{
    public required RuntimeOptions Runtime { get; set; }
    public required DebuggerOptions Debugger { get; set; }
    public required string WorkingDirectory { get; set; }
    public bool CreateWindow { get; set; } = false;

    /// <summary>An assembly the run's process loads first (<c>DOTNET_STARTUP_HOOKS</c>): Invoklet.Runtime's, which holds a script being
    /// debugged until its first stdin line comes; null for none.</summary>
    public string? StartupHook { get; set; }

    /// <summary>A copy for one run, with the startup hook its process loads.</summary>
    public ExecutionOptions WithStartupHook(string? hook) => new()
    {
        Runtime = Runtime,
        Debugger = Debugger,
        WorkingDirectory = WorkingDirectory,
        CreateWindow = CreateWindow,
        StartupHook = hook,
    };
}
