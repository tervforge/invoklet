using Invoklet.Cli;
using Spectre.Console;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Let RunAsync's own cancellation-token plumbing unwind (kill the child process, let go of the
    // build folder) instead of the runtime hard-killing this process on the spot.
    e.Cancel = true;
    cts.Cancel();
};

var stdout = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) });
var stderr = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

// Scripts are built in a folder kept for each, so an unchanged one isn't built again.
var services = CliServices.CreateDefault() with { BuildsFolder = CliServices.DefaultBuildsFolder };
return await CliRunner.RunAsync(args, services, stdout, stderr, cts.Token);
