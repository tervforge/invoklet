using System.Text;
using System.Threading.Channels;
using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures;

// A debug run over a fake netcoredbg (DAP frames pushed by the test) and a fake script that runs until the test ends it. The build folder
// holds Invoklet.Runtime.dll, the startup hook the script is given; it is the fixture's own and goes with it.
internal sealed class CompiledDotnetLanguageHostDebugAttachFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-tests-").FullName;

    public FakeDebugger Debugger { get; } = new();
    public FakeScript Script { get; } = new();
    public ExecutionOptions? ScriptOptions { get; private set; }

    public string StartupHook => Path.Combine(_folder, "Invoklet.Runtime.dll");

    public CompiledDotnetLanguageHostDebugAttachFixture() => File.WriteAllText(StartupHook, "");

    public CSharpLanguageHost CreateSut()
    {
        var options = new CompiledDotnetLanguageHostOptions
        {
            Platform = new DotnetSdk("net10.0", "net10.0", "10.0.0", "net10.0"),
            ExecutionOptions = new ExecutionOptions
            {
                WorkingDirectory = _folder,
                Runtime = new RuntimeOptions { RuntimePath = "dotnet" },
                Debugger = new DebuggerOptions { DebuggerPath = "netcoredbg" },
            },
            OutputDirectory = _folder,
            RuntimeLibPath = "Invoklet.Runtime.dll",
            Kind = MdRunKind.Statements,
        };
        var compiler = new FakeCompiler(new ProcessCompilerResult(Path.Combine(_folder, "out.dll"), true, [], []));
        return new CSharpLanguageHost(options, NullLoggerFactory.Instance, (_, _, _) => compiler,
            (scriptOptions, _, _, _, _) =>
            {
                ScriptOptions = scriptOptions;
                return Script;
            },
            _ => Debugger);
    }

    /// <summary>netcoredbg answers "initialize": the host goes on to attach.</summary>
    public void DebuggerInitialized() => Debugger.Push(new { seq = 1, type = "response", request_seq = 1, command = "initialize", success = true });

    /// <summary>netcoredbg answers "configurationDone": the debugger is ready.</summary>
    public void DebuggerConfigured() => Debugger.Push(new { seq = 2, type = "response", request_seq = 3, command = "configurationDone", success = true });

    /// <summary>What the host has written to netcoredbg, once it holds <paramref name="containing"/> (within two seconds).</summary>
    public async Task<string> SentToDebuggerAsync(string containing, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100; i++)
        {
            var written = Encoding.UTF8.GetString(Debugger.Input.ToArray());
            if (written.Contains(containing, StringComparison.Ordinal)) return written;
            await Task.Delay(20, cancellationToken);
        }
        return Encoding.UTF8.GetString(Debugger.Input.ToArray());
    }

    /// <summary>What the host has written on the script's stdin, once it wrote anything (within two seconds).</summary>
    public async Task<string> SentToScriptAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100 && Script.Input.Length == 0; i++) await Task.Delay(20, cancellationToken);
        return Encoding.UTF8.GetString(Script.Input.ToArray());
    }

    public void Dispose()
    {
        Script.Exit.TrySetResult();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class FakeCompiler(ProcessCompilerResult result) : IDotnetProcessCompiler
    {
        public Task<ProcessCompilerResult> Compilation(string code, CancellationToken ctx) => Task.FromResult(result);

        public void Dispose()
        {
        }
    }

    // netcoredbg: DAP frames out through a pipe that stays open, the host's requests in.
    internal sealed class FakeDebugger : IChildProcess
    {
        private readonly PipeStream _output = new();

        public MemoryStream Input { get; } = new();
        public StreamReader StandardOutput { get; } = new(new MemoryStream());
        public StreamReader StandardError { get; } = new(new MemoryStream());
        public Stream OutputStream => _output;
        public Stream InputStream => Input;

        public void Push(object body)
        {
            var json = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(body));
            _output.Push([.. Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n"), .. json]);
        }

        public bool Start() => true;
        public void Kill() { }
        public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
        public int ExitCode => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // The script, with the id the debugger attaches to; it runs until Exit is set.
    internal sealed class FakeScript : IChildProcess
    {
        public TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MemoryStream Input { get; } = new();

        public StreamReader StandardOutput { get; } = new(new MemoryStream());
        public StreamReader StandardError { get; } = new(new MemoryStream());
        public Stream OutputStream => StandardOutput.BaseStream;
        public Stream InputStream => Input;
        public int Id => 4242;

        public bool Start() => true;
        public void Kill() => Exit.TrySetResult();
        public Task WaitForExitAsync(CancellationToken ct) => Exit.Task.WaitAsync(ct);
        public int ExitCode => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PipeStream : Stream
    {
        private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>();

        public void Push(byte[] bytes)
        {
            foreach (var b in bytes) _channel.Writer.TryWrite(b);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken)) return 0;
            if (!_channel.Reader.TryRead(out var b)) return 0;
            buffer.Span[0] = b;
            return 1;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
