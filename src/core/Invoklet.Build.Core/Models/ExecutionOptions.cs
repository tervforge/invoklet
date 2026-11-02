namespace Invoklet.Build.Core.Models;

public class ExecutionOptions
{
    public required RuntimeOptions Runtime { get; set; }
    public required DebuggerOptions Debugger { get; set; }
    public required string WorkingDirectory { get; set; }
    public bool CreateWindow { get; set; } = false;

    /// <summary>The Secret Store's key for this run (<see cref="Secrets.SecretKeys.EnvVar"/> in the process it starts); null for a script that may not ask for secrets.</summary>
    public string? SecretKey { get; set; }

    /// <summary>A copy for one run, with the key it may be given.</summary>
    public ExecutionOptions WithSecretKey(string? key) => new()
    {
        Runtime = Runtime,
        Debugger = Debugger,
        WorkingDirectory = WorkingDirectory,
        CreateWindow = CreateWindow,
        SecretKey = key,
    };
}
