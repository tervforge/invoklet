using System.Text;
using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Contracts;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures;

// A host whose script writes one request for a secret or a path on its stdout and then waits; what the host reads, saves and asks is recorded,
// and its answer is what it writes on the script's stdin. The build folder is the fixture's own and goes with it.
internal sealed class CompiledDotnetLanguageHostSecretRequestFixture : IAsyncDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "Invoklet-tests-" + Guid.NewGuid());
    private CSharpLanguageHost? _host;

    public FakeChildProcess Process { get; private set; } = new("");

    public List<string> Read { get; } = [];
    public List<string> ReadPaths { get; } = [];
    public List<(string Name, string? Value)> Saved { get; } = [];
    public List<SecretAccess> Asked { get; } = [];
    public bool Prompted { get; private set; }

    // What the store holds for the secret read: a value, null for none, or a failure.
    public Func<string?> Store { get; set; } = () => "p@ss";

    // What the Path Store holds for the path read: a path, null for none, or a failure.
    public Func<string?> Paths { get; set; } = () => "/work/data";
    public bool HasSaver { get; set; } = true;
    public bool Allowed { get; set; } = true;

    /// <summary>The script's request, as Util.GetSecret, Util.SetSecret or Util.GetPath writes it.</summary>
    public static string Request(string json) => "\x1f" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) + "\n";

    public CSharpLanguageHost CreateSut(string request)
    {
        Process = new FakeChildProcess(request);
        var options = new CompiledDotnetLanguageHostOptions
        {
            Platform = new DotnetSdk("net10.0", "net10.0", "10.0.0", "net10.0"),
            ExecutionOptions = new ExecutionOptions
            {
                WorkingDirectory = _outputDir,
                Runtime = new RuntimeOptions { RuntimePath = "dotnet" },
                Debugger = new DebuggerOptions { DebuggerPath = "netcoredbg" },
            },
            OutputDirectory = _outputDir,
            RuntimeLibPath = "Invoklet.Runtime.dll",
            Kind = MdRunKind.Statements,
        };
        var compiler = new FakeCompiler(new ProcessCompilerResult("out.dll", true, [], []));
        var process = Process;
        _host = new CSharpLanguageHost(options, NullLoggerFactory.Instance, (_, _, _) => compiler,
            executionProcessFactory: (_, _, _, _, _) => process)
        {
            SecretReader = name =>
            {
                Read.Add(name);
                return Store();
            },
            PathReader = name =>
            {
                ReadPaths.Add(name);
                return Paths();
            },
            SecretAccessApprover = (access, _) =>
            {
                Asked.Add(access);
                return Task.FromResult(Allowed);
            },
        };
        if (HasSaver) _host.SecretSaver = (name, value) => Saved.Add((name, value));
        _host.InputRequested += (_, _) => Prompted = true;
        return _host;
    }

    /// <summary>What the host wrote on the script's stdin, once it wrote anything (within ten seconds).</summary>
    public async Task<string> AnswerAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Process.InputStream.Length == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10, cancellationToken);
        return Encoding.UTF8.GetString(Process.InputStream.ToArray());
    }

    /// <summary>The text of an answer that carries one (1 or 2 and Base64).</summary>
    public static string Payload(string answer) => Encoding.UTF8.GetString(Convert.FromBase64String(answer[1..^1]));

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.TerminateAsync(CancellationToken.None);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
    }

    private sealed class FakeCompiler(ProcessCompilerResult result) : IDotnetProcessCompiler
    {
        public Task<ProcessCompilerResult> Compilation(string code, CancellationToken ctx) => Task.FromResult(result);

        public void Dispose()
        {
        }
    }

    // The script: writes its request, then waits for the answer and never ends on its own.
    internal sealed class FakeChildProcess(string stdout) : IChildProcess
    {
        public StreamReader StandardOutput { get; } = new(new TextThenBlockingStream(stdout));
        public StreamReader StandardError { get; } = new(new MemoryStream());
        public Stream OutputStream { get; } = new MemoryStream();
        public MemoryStream InputStream { get; } = new();
        Stream IChildProcess.InputStream => InputStream;

        public bool Start() => true;

        public void Kill()
        {
        }

        public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
        public int ExitCode => 0;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TextThenBlockingStream(string text) : Stream
    {
        private readonly MemoryStream _text = new(Encoding.UTF8.GetBytes(text));

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
            var read = _text.Read(buffer.Span);
            if (read > 0) return read;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
