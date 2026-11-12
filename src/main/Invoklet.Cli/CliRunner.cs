using System.Text;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Spectre.Console;

namespace Invoklet.Cli;

// Compiles and runs a .inv file's code the same "fast path" the GUI's non-debug Run
// button uses (ILanguageHost.RunAsync, see EditorTabItemControl.RunAsync/BuildRunOptions) — no
// Avalonia, no DAP/netcoredbg. Structured as a static class (rather than top-level statements in
// Program.cs) so the orchestration itself is unit-testable via its IAnsiConsole/CancellationToken
// parameters, without needing a real console — stdout/stderr are IAnsiConsole (not a plain
// TextWriter) so .Dump() output can be rendered as a real Spectre.Console Table/Markup instead of
// plain text (user request — see DumpHtmlSpectreRenderer).
public static class CliRunner
{
    public static Task<int> RunAsync(
        string[] args, IAnsiConsole stdout, IAnsiConsole stderr, CancellationToken cancellationToken)
        => RunAsync(args, CliServices.CreateDefault(), stdout, stderr, cancellationToken);

    // One boundary for the whole command: a cancellation in any phase (reading the script, reading a connection's description,
    // compiling, running) is the same 130, and a file that cannot be read or written is a message, not a stack trace.
    public static async Task<int> RunAsync(
        string[] args, CliServices services, IAnsiConsole stdout, IAnsiConsole stderr, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(args, services, stdout, stderr, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Conventional shell exit code for a SIGINT-cancelled process (128 + SIGINT's 2).
            return CancelledExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return args.Length > 0 && string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase) ? 1 : NotRunExitCode;
        }
    }

    private static async Task<int> RunCoreAsync(
        string[] args, CliServices services, IAnsiConsole stdout, IAnsiConsole stderr, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            WriteUsage(stderr);
            return NotRunExitCode;
        }

        // The GUI's "File → Import LINQPad Query..." equivalent
        // for scripting/batch use. Unlike the GUI, overwrites the destination outright rather than
        // picking a free "Foo 2.inv" — a CLI tool re-run with the same arguments should be idempotent,
        // not silently rename around a leftover file from a previous run.
        if (string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
            return await ConvertLinqAsync(args, stdout, stderr, cancellationToken);

        var cli = CliArguments.Parse(args);
        if (cli.Error is not null)
        {
            stderr.WriteLine(cli.Error);
            return NotRunExitCode;
        }
        if (cli.ShowHelp)
        {
            WriteUsage(stdout);
            return 0;
        }
        if (cli.ShowVersion)
        {
            stdout.WriteLine(typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "unknown");
            return 0;
        }
        if (cli.ScriptPath is null)
        {
            WriteUsage(stderr);
            return NotRunExitCode;
        }

        var filePath = Path.GetFullPath(cli.ScriptPath);
        if (!File.Exists(filePath))
        {
            stderr.WriteLine($"File not found: {filePath}");
            return NotRunExitCode;
        }

        var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
        var parsed = MdRunParser.Parse(raw);
        if (!parsed.IsSuccess)
        {
            stderr.WriteLine($"Failed to parse '{filePath}' as a .inv document: {parsed.Error}");
            return NotRunExitCode;
        }

        var document = parsed.Document;
        if (cli.Language is { } language && language != document.FrontMatter.Language)
        {
            stderr.WriteLine($"'{filePath}' is a {document.FrontMatter.Language.ToLanguageId()} script, not {language.ToLanguageId()}.");
            return NotRunExitCode;
        }
        if (cli.Kind is { } kind) document.FrontMatter.Kind = kind;
        if (cli.TargetSdk is { } targetSdk) document.FrontMatter.TargetSdk = targetSdk;

        if (services.PluginDevelopmentEnabled && !cli.Quiet) stderr.WriteLine("Warning: plugin development mode is enabled.");
        foreach (var diagnostic in services.Plugins.Diagnostics)
        {
            if (cli.Quiet && !diagnostic.IsError) continue;
            stderr.WriteLine((diagnostic.IsError ? "error: " : "warning: ") + diagnostic.Message);
        }

        // The same choice as the window's: given arguments, else --args-set, the selected set or defaults.
        var arguments = MdRunArguments.Resolve(document.FrontMatter, cli.ScriptArguments, cli.ArgumentSet);
        if (arguments.Error is { } argumentsError)
        {
            stderr.WriteLine(argumentsError);
            return NotRunExitCode;
        }

        var languageId = document.FrontMatter.Language.ToLanguageId();

        var registry = services.Registry;
        if (!registry.IsRegistered(languageId))
        {
            stderr.WriteLine($"No runner registered for language '{languageId}'.");
            return NotRunExitCode;
        }

        // The SDK the script's targetSdk names — the same rule as the Invoklet window.
        var platform = TargetSdkResolver.Resolve(services.Sdks.Get(), document.FrontMatter.TargetSdk);
        if (platform is null)
        {
            stderr.WriteLine("No .NET SDK found (checked `dotnet --list-sdks`).");
            return NotRunExitCode;
        }
        if (!cli.Quiet && TargetSdkResolver.Warning(document.FrontMatter.TargetSdk, platform) is { } sdkWarning)
            stderr.WriteLine("warning: " + sdkWarning);

        if (services.BuildsFolder is { } builds) CliBuildFolder.PruneStale(builds, BuildFolderMaxAge);
        using var buildFolder = CliBuildFolder.Open(services.BuildsFolder, filePath);
        var outputDirectory = buildFolder.Path;
        // A gRPC server's classes are part of the build: read the ones not read yet or that failed,
        // or all of them again with --refresh-connections.
        // What takes a while before the script starts (packages, schemas) is told in a terminal, so a first run doesn't look stuck;
        // redirected, stderr stays the script's and the compiler's alone.
        var tellStages = !Console.IsErrorRedirected && !cli.Quiet;
        if (services.ConnectionSchemas is { } schemas && document.FrontMatter.Connections.Count > 0)
        {
            if (tellStages) stderr.WriteLine("Reading connection schemas: " + string.Join(", ", document.FrontMatter.Connections.Select(c => c.Name)));
            await schemas.EnsureAsync(document.FrontMatter.Connections, Path.GetDirectoryName(Path.GetFullPath(filePath)),
                cli.RefreshConnections ? ConnectionSchemaRefresh.All : ConnectionSchemaRefresh.Failed, cancellationToken);
        }
        var request = BuildRequest(document.FrontMatter, platform, outputDirectory, filePath, cli.ScriptArguments,
            services.ConnectionDrivers, services.ConnectionSchemas?.Cache, cli.ArgumentSet);

        RunResult result;
        await using var host = registry.Create(languageId, request);
        if (tellStages)
            host.StageChanged += (stage, detail) =>
            {
                if (stage is RunStage.RestoringPackages or RunStage.GeneratingConnections && detail is not null)
                    stderr.WriteLine((stage == RunStage.RestoringPackages ? "Restoring packages: " : "Generating connections: ") + detail);
            };
        // Util.ReadLine and a missing secret's question are asked in the terminal: the prompt on stderr, the answer from stdin.
        host.InputRequested += (prompt, isPassword) => _ = Task.Run(async () =>
        {
            if (!string.IsNullOrEmpty(prompt)) stderr.Write(prompt + " ");
            // The end of stdin answers with an empty line: nothing else would let the script go on.
            var answer = (isPassword && !Console.IsInputRedirected ? ReadMasked() : Console.In.ReadLine()) ?? "";
            await host.SendInputAsync(answer, cancellationToken);
        }, cancellationToken);
        if (cli.CompileOnly)
        {
            var compilation = await host.CompileAsync(document.RunCode, cancellationToken);
            foreach (var diagnostic in compilation.Diagnostics)
            {
                if (cli.Quiet && diagnostic.Severity != DiagnosticSeverity.Error) continue;
                stderr.WriteLine(FormatDiagnostic(diagnostic));
            }
            return compilation.Success ? 0 : NotRunExitCode;
        }

        // Whether the script's stdout is in the middle of a line (Console.Write without a
        // line break): a Dump then starts on a new line instead of being glued to that text.
        var stdoutMidLine = false;
        result = await host.RunAsync(document.RunCode, (data, outputKind) =>
        {
            switch (outputKind)
            {
                case OutputKind.StandardOutput:
                    stdout.WriteLine(data);
                    stdoutMidLine = false;
                    break;
                case OutputKind.StandardOutputPartial:
                    stdout.Write(data);
                    stdoutMidLine = data.Length > 0;
                    break;
                case OutputKind.StandardError:
                    stderr.WriteLine(data);
                    break;
                case OutputKind.StandardErrorPartial:
                    stderr.Write(data);
                    break;
                // Compiler warnings are not script output: stdout carries only what the script wrote.
                case OutputKind.Diagnostic:
                    if (!cli.Quiet) stderr.WriteLine(data);
                    break;
                case OutputKind.DumpHtml:
                    var html = Encoding.UTF8.GetString(Convert.FromBase64String(data));
                    if (stdoutMidLine) stdout.WriteLine();
                    stdoutMidLine = false;
                    stdout.Write(DumpHtmlSpectreRenderer.Render(html));
                    break;
            }
        }, cancellationToken);

        if (result.Outcome == RunOutcome.StartFailed)
            stderr.WriteLine("Failed to start the compiled script.");
        return ExitCodeFor(result);
    }

    // As long as the window keeps the build folder of a script nobody opens.
    private static readonly TimeSpan BuildFolderMaxAge = TimeSpan.FromDays(14);

    // A password typed in the terminal, not echoed.
    private static string ReadMasked()
    {
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return typed.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0) typed.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }

    // The exit code when the script never ran: a bad command line, file or SDK, or a compile error —
    // the same 2 as NetPad's npad, so a caller can tell it from a script that ran and failed.
    internal const int NotRunExitCode = 2;

    internal const int CancelledExitCode = 130;

    private static void WriteUsage(IAnsiConsole console)
    {
        console.WriteLine("Usage: Invoklet [run] [options] <script.inv> [--] [script arguments...]");
        console.WriteLine("       Invoklet convert <input.linq> [output.inv]");
        console.WriteLine();
        console.WriteLine("Options:");
        console.WriteLine("  -lang=<kind>, -k, --kind <kind>   Statements, Program or Expression (s, p, e)");
        console.WriteLine("  -s, --sdk <version>               The .NET SDK to run on, such as 9 or net9.0");
        console.WriteLine("  -compileonly, --compile-only      Compile the script without running it");
        console.WriteLine("  --refresh-connections             Read the connections' descriptions (gRPC services) again");
        console.WriteLine("  -q, --quiet                       Show no warnings, only errors and the script's output");
        console.WriteLine("  --args-set <name>                 Run with the script's argument set of that name");
        console.WriteLine("  --plugins-dev <directory>         Load development plugins from the specified directory");
        console.WriteLine("  -h, --help                        Show this help");
        console.WriteLine("  --version                         Show the version");
        console.WriteLine();
        console.WriteLine("What follows the script is passed to it as args, but the options above are");
        console.WriteLine("still read there until --; everything after -- goes to the script as it is.");
        console.WriteLine("Arguments given here replace the script's; a bare -- runs it with none.");
    }

    private static string FormatDiagnostic(IDiagnostic d)
    {
        var position = d.Line > 0 ? $"({d.Line},{d.Column}): " : "";
        var code = d.Code.Length > 0 ? $" {d.Code}" : "";
        return $"{position}{d.Severity.ToString().ToLowerInvariant()}{code}: {d.Message}";
    }

    // The script's own exit code is the CLI's, so `Environment.Exit(7)` gives 7 and text on
    // stderr alone (a warning, a log line) doesn't fail the run. Not compiling or not starting is 2.
    internal static int ExitCodeFor(RunResult result)
        => result.Outcome == RunOutcome.Exited ? result.ExitCode ?? 1 : NotRunExitCode;

    internal static async Task<int> ConvertLinqAsync(
        string[] args, IAnsiConsole stdout, IAnsiConsole stderr, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            stderr.WriteLine("Usage: Invoklet convert <input.linq> [output.inv]");
            return 1;
        }

        var linqPath = Path.GetFullPath(args[1]);
        if (!File.Exists(linqPath))
        {
            stderr.WriteLine($"File not found: {linqPath}");
            return 1;
        }

        var outputPath = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(Path.GetDirectoryName(linqPath)!, Path.GetFileNameWithoutExtension(linqPath) + ".inv");

        MdRunDocument document;
        try
        {
            var linqText = await File.ReadAllTextAsync(linqPath, cancellationToken);
            document = LinqConverter.Convert(linqText, title: Path.GetFileNameWithoutExtension(linqPath));
        }
        catch (FormatException ex)
        {
            stderr.WriteLine($"Failed to convert '{linqPath}': {ex.Message}");
            return 1;
        }

        await File.WriteAllTextAsync(outputPath, MdRunWriter.Write(document), cancellationToken);
        stdout.WriteLine($"Wrote {outputPath}");
        return 0;
    }

    // The run the script's front matter asks for, built as the window builds its own: the language's
    // backend resolves its references (relative to the script, macros expanded) and package sources.
    // Arguments given on the command line replace the front matter's. The process starts in the
    // build folder; the PATH's dotnet runs it, and there is no debugger on this path
    // (ILanguageHost.RunAsync, not RunDebugAsync).
    internal static ExecutionRequest BuildRequest(
        MdRunFrontMatter frontMatter, DotnetSdk platform, string outputDirectory,
        string? currentQueryPath = null, IReadOnlyList<string>? arguments = null, ConnectionDriverRegistry? connectionDrivers = null,
        ConnectionSchemaCache? connectionSchemas = null, string? argumentSet = null)
        => ExecutionRequest.For(frontMatter, currentQueryPath, arguments, argumentSet) with
        {
            Connections = GeneratedConnections.For(frontMatter.Connections, frontMatter.Language, frontMatter.Packages.Keys,
                connectionDrivers ?? new ConnectionDriverRegistry(), connectionSchemas,
                currentQueryPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(currentQueryPath))),
            Platform = platform,
            OutputDirectory = outputDirectory,
            WorkingDirectory = outputDirectory,
        };
}
