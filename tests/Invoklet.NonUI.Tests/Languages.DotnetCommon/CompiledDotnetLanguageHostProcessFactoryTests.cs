using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Compiler;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon;

// RunAsync/RunDebugAsync used to always ChildProcess.ForExecution(...)/
// ForDebugger(...) directly, so exercising them meant either shelling out to a real dotnet/netcoredbg
// process or not testing them at all — the same gap CompiledDotnetLanguageHostCompilerFactoryTests
// already closed for the compiler seam. The injected ExecutionChildProcessFactory/
// DebuggerChildProcessFactory let a fake process stand in instead.
public class CompiledDotnetLanguageHostProcessFactoryTests
{
    private sealed class FakeCompiler(ProcessCompilerResult result) : IDotnetProcessCompiler
    {
        public Task<ProcessCompilerResult> Compilation(string code, CancellationToken ctx) => Task.FromResult(result);

        public void Dispose()
        {
        }
    }

    // Empty in-memory streams are enough here: DrainReaderAsync's ReadLineAsync (Run) and
    // DapSession's ReadMessageAsync (RunDebug) both treat an immediate EOF as "nothing more to read"
    // and return cleanly — no real dotnet/netcoredbg process, no protocol scripting needed.
    private sealed class FakeChildProcess : IChildProcess
    {
        public bool Started { get; private set; }

        public StreamReader StandardOutput { get; } = new(new MemoryStream());
        public StreamReader StandardError { get; } = new(new MemoryStream());
        public Stream OutputStream { get; } = new MemoryStream();
        public Stream InputStream { get; } = new MemoryStream();

        public bool Start()
        {
            Started = true;
            return true;
        }

        public void Kill()
        {
        }

        public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
        public int ExitCode => 0;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (CSharpLanguageHost Host, string OutputDir) CreateHost(
        ExecutionChildProcessFactory? executionProcessFactory = null,
        DebuggerChildProcessFactory? debuggerProcessFactory = null,
        string? currentQueryPath = null,
        IReadOnlyList<string>? arguments = null,
        string binary = "out.dll")
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "Invoklet-tests-" + Guid.NewGuid());
        var options = new CompiledDotnetLanguageHostOptions
        {
            Platform = new DotnetSdk("net10.0", "net10.0", "10.0.0", "net10.0"),
            ExecutionOptions = new ExecutionOptions
            {
                WorkingDirectory = outputDir,
                Runtime = new RuntimeOptions { RuntimePath = "dotnet" },
                Debugger = new DebuggerOptions { DebuggerPath = "netcoredbg" },
            },
            OutputDirectory = outputDir,
            RuntimeLibPath = "Invoklet.Runtime.dll",
            Kind = MdRunKind.Statements,
            CurrentQueryPath = currentQueryPath,
            Arguments = arguments ?? [],
        };

        var compiler = new FakeCompiler(new ProcessCompilerResult(binary, true, [], []));
        var host = new CSharpLanguageHost(
            options, NullLoggerFactory.Instance, (_, _, _) => compiler, executionProcessFactory, debuggerProcessFactory);
        return (host, outputDir);
    }

    [Fact]
    public async Task RunAsync_Should_UseTheInjectedExecutionProcess_InsteadOfShellingOutToDotnet()
    {
        FakeChildProcess? received = null;
        string? receivedAssemblyPath = null;
        ExecutionChildProcessFactory factory = (options, assemblyPath, _, _, _) =>
        {
            receivedAssemblyPath = assemblyPath;
            return received = new FakeChildProcess();
        };

        var (host, outputDir) = CreateHost(executionProcessFactory: factory);
        try
        {
            await host.RunAsync("int x = 1;", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(received);
            Assert.True(received.Started);
            Assert.Equal("out.dll", receivedAssemblyPath);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    // Confirms CompiledDotnetLanguageHostBase.RunAsync
    // actually forwards Options.CurrentQueryPath into the process factory (ChildProcess.ForExecution's
    // own handling of it is tested separately in ChildProcessCurrentQueryPathTests.cs, without needing
    // a whole host/compile pipeline).
    [Fact]
    public async Task RunAsync_Should_PassCurrentQueryPath_ToTheExecutionProcessFactory()
    {
        string? received = null;
        ExecutionChildProcessFactory factory = (_, _, _, currentQueryPath, _) =>
        {
            received = currentQueryPath;
            return new FakeChildProcess();
        };

        var (host, outputDir) = CreateHost(executionProcessFactory: factory, currentQueryPath: "/root/a.inv");
        try
        {
            await host.RunAsync("int x = 1;", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("/root/a.inv", received);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task RunAsync_Should_PassTheScriptArguments_ToTheExecutionProcessFactory()
    {
        IReadOnlyList<string>? received = null;
        ExecutionChildProcessFactory factory = (_, _, _, _, arguments) =>
        {
            received = arguments;
            return new FakeChildProcess();
        };

        var (host, outputDir) = CreateHost(executionProcessFactory: factory, arguments: ["a", "b c"]);
        try
        {
            await host.RunAsync("int x = 1;", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["a", "b c"], received);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task RunDebugAsync_Should_UseTheInjectedDebuggerProcess_InsteadOfShellingOutToNetcoredbg()
    {
        FakeChildProcess? received = null;
        DebuggerChildProcessFactory factory = _ => received = new FakeChildProcess();

        var (host, outputDir) = CreateHost(debuggerProcessFactory: factory);
        try
        {
            await host.RunDebugAsync(
                "int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);

            Assert.NotNull(received);
            Assert.True(received.Started);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }
}
