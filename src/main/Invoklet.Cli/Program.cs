using Invoklet.BuiltIns;
using Invoklet.Cli;
using Invoklet.Plugins.Hosting;
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

PluginDevelopmentOptions plugins;
try { plugins = PluginStartup.ParseArguments(args); }
catch (ArgumentException ex)
{
    stderr.WriteLine($"error: {ex.Message}");
    return CliRunner.NotRunExitCode;
}

if (plugins.Directory is not null) stderr.WriteLine("Warning: plugin development mode is enabled.");
// Scripts are built in a folder kept for each, so an unchanged one isn't built again.
var services = CliServices.CreateDefault(plugins.Directory) with { BuildsFolder = CliServices.DefaultBuildsFolder };
foreach (var diagnostic in services.Plugins.Diagnostics) stderr.WriteLine(diagnostic.Message);
return await CliRunner.RunAsync(plugins.RemainingArguments.ToArray(), services, stdout, stderr, cts.Token);
