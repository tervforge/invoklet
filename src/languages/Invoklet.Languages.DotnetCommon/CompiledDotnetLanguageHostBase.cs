using System.Text;
using System.Text.Json;
using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Contracts;
using Invoklet.Contracts.Debugging;
using Invoklet.Contracts.Models;
using Invoklet.Debug;
using Invoklet.Format.MdRun;
using Invoklet.Languages.DotnetCommon.Adapters;
using Invoklet.Languages.DotnetCommon.Compiler;
using Invoklet.Secrets;
using Microsoft.Extensions.Logging;

namespace Invoklet.Languages.DotnetCommon;

/// <summary>
/// Builds the <see cref="IDotnetProcessCompiler"/> a <see cref="CompiledDotnetLanguageHostBase"/> uses
/// to compile a given run — injectable seam so tests can substitute a fake compiler
/// for the language's own in-process Roslyn compiler (<see cref="CreateCompiler"/>).
/// </summary>
public delegate IDotnetProcessCompiler DotnetProcessCompilerFactory(
    DotnetLanguageProjectProfile profile, CompiledDotnetLanguageHostOptions options, ILoggerFactory loggerFactory);

/// <summary>
/// Builds the <see cref="IChildProcess"/> a <see cref="CompiledDotnetLanguageHostBase"/> runs a compiled
/// assembly in — injectable seam, mirroring <see cref="DotnetProcessCompilerFactory"/>,
/// so tests can substitute a fake process instead of always shelling out to <c>dotnet</c>.
/// </summary>
public delegate IChildProcess ExecutionChildProcessFactory(
    ExecutionOptions options, string assemblyPath, string parentPid, string? currentQueryPath,
    IReadOnlyList<string> arguments);

/// <summary>
/// Builds the <see cref="IChildProcess"/> a <see cref="CompiledDotnetLanguageHostBase"/> runs netcoredbg
/// in for a debug session — same rationale as <see cref="ExecutionChildProcessFactory"/>.
/// </summary>
public delegate IChildProcess DebuggerChildProcessFactory(ExecutionOptions options);

/// <summary>
/// Shared compile/run/debug orchestration for every .NET language compiled the same way (in-process
/// Roslyn, <see cref="RoslynScriptCompiler"/>, with debugging over DAP against netcoredbg) — was duplicated near-verbatim between CSharpLanguageHost and VbLanguageHost before
/// this base class existed. A concrete language host only needs to supply
/// <see cref="Language"/>/<see cref="ILanguageHost.Capabilities"/> (from <see cref="LanguageHostBase"/>),
/// <see cref="ProjectProfile"/>, <see cref="CreateCompiler"/>, <see cref="ApplyKindTransform"/> and <see cref="CalcKindOffset"/> —
/// see CSharpLanguageHost/VbLanguageHost for how small that makes each one.
/// </summary>
public abstract class CompiledDotnetLanguageHostBase : LanguageHostBase
{
    // New format: Record Separator + base64 — see Invoklet.Runtime.ObjectExtensions.DumpLinePrefixChar.
    private const char RsPrefix = '\x1e';

    // ASCII Unit Separator + base64 JSON — see Invoklet.Runtime.Util.InputRequestPrefixChar.
    private const char InputRequestPrefix = '\x1f';

    // ASCII Group Separator — see Invoklet.Runtime.Util.CancelListeningPrefixChar: the script asked for
    // Util.CancellationToken, so Stop asks it to finish (RequestStop) before ending it.
    private const char CancelListeningPrefix = '\x1d';

    // How long a script that listens for Stop gets to finish on its own before it is ended.
    internal static readonly TimeSpan DefaultStopGracePeriod = TimeSpan.FromSeconds(3);

    // Of this host only; a test shortens it.
    internal TimeSpan StopGracePeriod { get; set; } = DefaultStopGracePeriod;

    private static readonly TimeSpan OutputAfterExit = TimeSpan.FromSeconds(2);

    private volatile bool _scriptListensForStop;

    private readonly CompiledDotnetLanguageHostOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly DotnetProcessCompilerFactory _compilerFactory;
    private readonly ExecutionChildProcessFactory _executionProcessFactory;
    private readonly DebuggerChildProcessFactory _debuggerProcessFactory;
    private CancellationTokenSource? _runCts;
    private IDapSession? _currentSession;
    private Stream? _currentInputStream;

    private IReadOnlyList<IVariableInfo> _lastDebugVariables = [];

    /// <summary>This host's own front-matter-derived options — available to <see cref="ApplyKindTransform"/>/<see cref="CalcKindOffset"/> overrides.</summary>
    protected CompiledDotnetLanguageHostOptions Options => _options;

    /// <summary>This language's project-file/import-syntax conventions — see <see cref="DotnetLanguageProjectProfile"/>.</summary>
    protected abstract DotnetLanguageProjectProfile ProjectProfile { get; }

    // Where RoslynScriptCompiler writes the source and the DAP session expects breakpoints:
    // OutputDirectory/{OutputName}{the language's extension}.
    public override string SourceFilePath =>
        Path.Combine(_options.OutputDirectory, _options.OutputName + ProjectProfile.SourceFileExtension);

    /// <summary>
    /// Rewrites the user's code per <see cref="MdRunKind"/> before compiling — public (not just
    /// protected) so it stays directly unit-testable against a concrete host the way it always was,
    /// without requiring a test-only subclass. Must match <see cref="CalcKindOffset"/> exactly: the
    /// prefix this inserts is what DapSession maps a user-visible breakpoint line onto in the
    /// compiled source.
    /// </summary>
    public abstract string ApplyKindTransform(string code);

    /// <summary>
    /// Lines prepended before the user's own first line — both by <see cref="ApplyKindTransform"/>
    /// and by the compiler's own per-script global-usings/imports prepend (<see cref="Options"/>.Usings.Count).
    /// Depends on <paramref name="code"/>, the user's code before the transform, where the transform does.
    /// </summary>
    public abstract int CalcKindOffset(string code);

    protected CompiledDotnetLanguageHostBase(
        CompiledDotnetLanguageHostOptions options,
        ILoggerFactory loggerFactory,
        DotnetProcessCompilerFactory? compilerFactory = null,
        ExecutionChildProcessFactory? executionProcessFactory = null,
        DebuggerChildProcessFactory? debuggerProcessFactory = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _compilerFactory = compilerFactory ?? ((_, o, lf) => CreateCompiler(o, lf));
        _executionProcessFactory = executionProcessFactory
            ?? new ExecutionChildProcessFactory((options, assemblyPath, parentPid, currentQueryPath, arguments)
                => ChildProcess.ForExecution(options, assemblyPath, parentPid, currentQueryPath, arguments,
                    _options.ArgumentContractJson));
        _debuggerProcessFactory = debuggerProcessFactory ?? ChildProcess.ForDebugger;
        Directory.CreateDirectory(options.OutputDirectory);
    }

    // The language's compiler: in-process Roslyn (RoslynScriptCompiler) — Invoklet doesn't use MSBuild.
    protected abstract IDotnetProcessCompiler CreateCompiler(CompiledDotnetLanguageHostOptions options, ILoggerFactory loggerFactory);

    /// <summary>Checks the user's source before any kind transform. Languages can reject identifiers
    /// reserved by generated code while diagnostics still point at the original source.</summary>
    protected virtual IReadOnlyList<ResultItem> ValidateUserCode(string code) => [];

    // ── Compile ──────────────────────────────────────────────────────────────

    public override async Task<ICompilationResult> CompileAsync(
        string code, CancellationToken cancellationToken = default)
    {
        (CompiledDotnetCompilationResult result, _) = await CompileUserCodeAsync(code, cancellationToken);
        return result;
    }

    private Task<(CompiledDotnetCompilationResult result, string? binaryPath)> CompileUserCodeAsync(
        string code, CancellationToken ct)
    {
        var validationErrors = ValidateUserCode(code);
        if (validationErrors.Count == 0)
            return CompileInternalAsync(code, ApplyKindTransform(code), ct);

        var failed = new ProcessCompilerResult("", false, validationErrors, []);
        return Task.FromResult<(CompiledDotnetCompilationResult, string?)>((new CompiledDotnetCompilationResult(failed), null));
    }

    private async Task<(CompiledDotnetCompilationResult result, string? binaryPath)> CompileInternalAsync(
        string userCode, string code, CancellationToken ct)
    {
        using var compiler = _compilerFactory(ProjectProfile, _options, _loggerFactory);
        StageChangedHandler forward = OnStageChanged;
        compiler.StageChanged += forward;

        ProcessCompilerResult raw;
        try
        {
            raw = await compiler.Compilation(code, ct);
        }
        finally
        {
            compiler.StageChanged -= forward;
        }
        CompiledDotnetCompilationResult result = new(MapToUserCode(userCode, raw));
        if (raw.Success)
            OnCompiled(raw.OutputBinaryFile);
        return (result, raw.Success ? raw.OutputBinaryFile : null);
    }

    // The compiler reports positions in the generated script file: the front-matter imports and the
    // kind transform's wrapper lines come before the user's first line. Only diagnostics located in
    // that file are translated; the generated and connection sources, generator output and
    // position-less messages keep what the compiler reported.
    private ProcessCompilerResult MapToUserCode(string userCode, ProcessCompilerResult raw)
    {
        if (raw.Errors.Count == 0 && raw.Warnings.Count == 0) return raw;
        return raw with
        {
            Errors = [.. raw.Errors.Select(item => MapToUserCode(userCode, item))],
            Warnings = [.. raw.Warnings.Select(item => MapToUserCode(userCode, item))],
        };
    }

    private ResultItem MapToUserCode(string userCode, ResultItem item)
    {
        if (item.LineSymbol.Line == 0 || !IsScriptFile(item.File)) return item;
        var mapped = MapToUserPosition(userCode, (int)item.LineSymbol.Line, (int)item.LineSymbol.Symbol);
        // A position in generated text has no counterpart in the user's code: report the message
        // without a location rather than a misleading one.
        var position = mapped is { } p ? new LineSymbol((uint)p.Line, (uint)p.Column) : new LineSymbol(0, 0);
        return item with { LineSymbol = position };
    }

    // The compiler names the script file relative to OutputDirectory, with a leading separator.
    private bool IsScriptFile(string file) =>
        string.Equals(file.TrimStart('/', '\\'), Path.GetFileName(SourceFilePath), StringComparison.Ordinal);

    /// <summary>
    /// Translates a 1-based position in the compiled script file to the same position in the user's
    /// code (<paramref name="userCode"/>, before <see cref="ApplyKindTransform"/>), or null when it
    /// lies in text Invoklet generated: the front-matter imports and the kind transform's wrapper.
    /// The default assumes the transform only inserts whole lines before the user's code, as
    /// <see cref="CalcKindOffset"/> counts them; a language whose transform moves user lines overrides it.
    /// </summary>
    protected virtual (int Line, int Column)? MapToUserPosition(string userCode, int line, int column)
    {
        var userLine = line - CalcKindOffset(userCode);
        return userLine >= 1 && userLine <= UserLineCount(userCode) ? (userLine, column) : null;
    }

    // The lines the kind transform keeps from the user's code: Expression drops trailing whitespace.
    private int UserLineCount(string userCode) =>
        1 + (Options.Kind == MdRunKind.Expression ? userCode.TrimEnd() : userCode).Count(c => c == '\n');

    // ── Run ──────────────────────────────────────────────────────────────────

    public override async Task<RunResult> RunAsync(
        string code,
        OutputReceivedHandler? outputHandler = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        (CompiledDotnetCompilationResult compilationResult, string? binaryPath) =
            await CompileUserCodeAsync(code, cancellationToken);
        EmitDiagnostics(compilationResult.Diagnostics, outputHandler);
        if (!compilationResult.Success || binaryPath is null) return RunResult.CompilationFailed;

        // No `using` here (same pattern as the other CTS Cancel+Dispose fixes) —
        // TerminateAsync can call _runCts.Cancel() from another thread (a "Stop" click racing this
        // method's own natural completion); disposing here would let that call land on an
        // already-disposed CTS and throw ObjectDisposedException. A CTS with no timer holds no
        // resources, so simply never disposing it is safe.
        // Not linked to cancellationToken: cancelling that (Stop) ends the run through StopAsync,
        // which first gives a script that listens for it a moment to finish.
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _scriptListensForStop = false;

        try
        {
            await using var process = _executionProcessFactory(
                _options.ExecutionOptions,
                binaryPath,
                Environment.ProcessId.ToString(),
                _options.CurrentQueryPath,
                _options.Arguments);

            if (!process.Start()) return RunResult.StartFailed;
            OnStageChanged(RunStage.Running);
            await using var stop = cancellationToken.Register(() => _ = StopAsync(process, cts));

            _currentInputStream = process.InputStream;
            var output = new OutputDrain(WrapWithSentinelDetection(outputHandler), outputHandler);
            var drained = Task.WhenAll(
                output.DrainAsync(process.StandardOutput, OutputKind.StandardOutput, cts.Token),
                output.DrainAsync(process.StandardError, OutputKind.StandardError, cts.Token));

            // The run ends when the process exits, not when its output closes — a script
            // that closes stdout/stderr itself is still running, and disposing the process here
            // would kill it. Both pipes are still drained to EOF (concurrently, BUG-1) so no output
            // is lost; a failed drain stops waiting at once (the disposal below then kills the script).
            var exited = process.WaitForExitAsync(cts.Token);
            if (await Task.WhenAny(drained, exited) == drained && drained.IsFaulted)
                await drained;
            await exited;
            await drained;

            // Stopped by the user, even if the script then finished on its own within the grace period.
            cancellationToken.ThrowIfCancellationRequested();
            return RunResult.Exited(process.ExitCode);
        }
        finally
        {
            _runCts = null;
            _currentInputStream = null;
        }
    }

    // Stop: a script that listens for it (Util.CancellationToken) is asked to finish and given
    // StopGracePeriod to exit; then — or at once, for any other script — the run is ended.
    private async Task StopAsync(IChildProcess process, CancellationTokenSource run)
    {
        if (_scriptListensForStop && process.RequestStop())
        {
            using var grace = new CancellationTokenSource(StopGracePeriod);
            try
            {
                await process.WaitForExitAsync(grace.Token);
                // It finished: what it printed on the way out is still in the pipes. The run ends when they are read, or a moment later
                // if another process keeps them open.
                run.CancelAfter(OutputAfterExit);
                return;
            }
            catch (OperationCanceledException)
            {
                // Didn't finish in time.
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }
        run.Cancel();
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

    public override async Task RunDebugAsync(
        string code,
        OutputReceivedHandler outputHandler,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var lineOffset = CalcKindOffset(code);

        (CompiledDotnetCompilationResult compilationResult, string? binaryPath) =
            await CompileUserCodeAsync(code, cancellationToken);
        EmitDiagnostics(compilationResult.Diagnostics, outputHandler);
        if (!compilationResult.Success || binaryPath is null) return;

        // See RunAsync's own linked CTS above for why this isn't a `using` too.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCts = cts;

        try
        {
            // The script is started here as for a run, with its own stdin and stdout, so it can ask for input and its output reaches the
            // same handler; the debugger attaches to it. Invoklet.Runtime's startup hook holds it until the breakpoints are set.
            var hook = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(binaryPath)!, "Invoklet.Runtime.dll"));
            var scriptOptions = _options.ExecutionOptions.WithStartupHook(File.Exists(hook) ? hook : null);
            await using var script = _executionProcessFactory(
                scriptOptions, binaryPath, Environment.ProcessId.ToString(), _options.CurrentQueryPath, _options.Arguments);
            if (!script.Start()) throw new InvalidOperationException("Failed to start the script.");
            _currentInputStream = script.InputStream;
            var output = new OutputDrain(WrapWithSentinelDetection(outputHandler), outputHandler);
            var drained = Task.WhenAll(
                output.DrainAsync(script.StandardOutput, OutputKind.StandardOutput, cts.Token),
                output.DrainAsync(script.StandardError, OutputKind.StandardError, cts.Token));

            // netcoredbg sends nothing when an attached process ends: the session ends with the script.
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var exited = script.WaitForExitAsync(cts.Token);
            _ = exited.ContinueWith(_ => sessionCts.Cancel(), TaskScheduler.Default);

            await using var process = _debuggerProcessFactory(_options.ExecutionOptions);
            var sessionOptions = new DapSessionOptions(
                ProgramPath: binaryPath,
                WorkingDirectory: _options.OutputDirectory,
                Breakpoints: ActiveBreakpoints,
                SourceFileExtension: ProjectProfile.SourceFileExtension,
                LineOffset: lineOffset,
                BreakOnAllExceptions: _options.BreakOnAllExceptions,
                BreakOnUncaughtExceptions: _options.BreakOnUncaughtExceptions,
                CurrentQueryPath: _options.CurrentQueryPath,
                RuntimePath: _options.ExecutionOptions.Runtime.RuntimePath,
                Arguments: _options.Arguments,
                ArgumentContractJson: _options.ArgumentContractJson,
                AttachProcessId: script.Id);
            OnStageChanged(RunStage.Running);
            await using var session = new DapSession(
                process,
                sessionOptions,
                outputHandler,
                HandleBreakpointHit,
                _loggerFactory.CreateLogger<DapSession>(),
                OnBreakpointVerified,
                ReleaseScriptAsync);

            _currentSession = session;
            try
            {
                await session.RunAsync(sessionCts.Token);
            }
            catch (OperationCanceledException) when (exited.IsCompleted && !cts.IsCancellationRequested)
            {
                // The script ended: the session was ended with it.
            }
            finally
            {
                session.FlushOutputBuffers();
                _currentSession = null;
            }
            // The debugger is gone (it ended or failed before the script did): the debug run ends with it, so a script still waiting
            // to be let go, or paused, does not keep it open.
            if (!exited.IsCompleted) script.Kill();
            await exited;
            await drained;

            async Task ReleaseScriptAsync(CancellationToken ct)
            {
                if (scriptOptions.StartupHook is null) return;
                await script.InputStream.WriteAsync("\n"u8.ToArray(), ct);
                await script.InputStream.FlushAsync(ct);
            }
        }
        finally
        {
            _runCts = null;
            _currentInputStream = null;
        }
    }

    public override async Task PauseAsync(CancellationToken cancellationToken)
    {
        EnsureCapability(LanguageHostCapabilities.Debug);
        if (_currentSession is { } s)
            await s.PauseAsync(cancellationToken);
    }

    // Forward gutter breakpoint edits made during an active debug session to the DAP session, so
    // netcoredbg's own breakpoint set doesn't go stale relative to what's shown in the editor.
    // The resend itself only happens the next time execution naturally stops (see
    // DapSession.RequestBreakpointResync) — sending "setBreakpoints" while the debuggee is
    // running would race the session's single message-read loop.
    public override void AddBreakpoint(IBreakpoint breakpoint)
    {
        base.AddBreakpoint(breakpoint);
        _currentSession?.RequestBreakpointResync();
    }

    public override void RemoveBreakpoint(IBreakpoint breakpoint)
    {
        base.RemoveBreakpoint(breakpoint);
        _currentSession?.RequestBreakpointResync();
    }

    private void HandleBreakpointHit(IBreakpointHitContext context)
    {
        _lastDebugVariables = context.LocalVariables;
        OnBreakpointHit(context);
    }

    // ── Terminate ─────────────────────────────────────────────────────────────

    public override Task TerminateAsync(CancellationToken cancellationToken)
    {
        _runCts?.Cancel();
        return Task.CompletedTask;
    }

    // ── Memory dump ───────────────────────────────────────────────────────────

    public override Task<IMemoryDump> GetMemoryDumpAsync(CancellationToken cancellationToken)
    {
        EnsureCapability(LanguageHostCapabilities.MemoryDump);

        IMemoryDump dump = new DapMemoryDump
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Variables = _lastDebugVariables,
        };
        return Task.FromResult(dump);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public override async ValueTask DisposeAsync()
    {
        // RunAsync/RunDebugAsync never dispose _runCts themselves (see the comment where it's
        // created), so there's nothing here that can throw ObjectDisposedException anymore.
        if (_runCts is not null)
            await _runCts.CancelAsync();

        await base.DisposeAsync();
    }

    // ── Interactive input ────────────────────────────────────────────────────

    // No-op if no script is running (RunAsync or RunDebugAsync, which both start it with a stdin of its own).
    public override async Task SendInputAsync(string text, CancellationToken cancellationToken)
    {
        if (_currentInputStream is not { } stream) return;
        // The answer to a missing secret is kept before the script gets it, so it is there next time.
        // An empty answer is no secret (the script reports it missing), so nothing is kept.
        if (Interlocked.Exchange(ref _pendingSecret, null) is { } secret && text.Length > 0 && SecretSaver is { } save)
        {
            try
            {
                await Task.Run(() => save(secret, text), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The script still gets its value; only keeping it for next time failed.
                _loggerFactory.CreateLogger<CompiledDotnetLanguageHostBase>().LogWarning(ex, "Could not keep the secret '{Name}' in the Secret Store", secret);
            }
        }
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Recognizes both sentinel-prefixed lines a running script can emit on stdout: RsPrefix for
    // ObjectExtensions.Dump (DumpHtml, unconditional) and InputRequestPrefix for Util.ReadLine/
    // GetSecret/SetSecret (only while _currentInputStream is set, i.e. while a script Invoklet started runs).
    //
    // A record is recognized anywhere in a stdout line, not only at its start: a script that wrote
    // text without a line break (Console.Write) before calling Dump/ReadLine leaves the record after
    // that text. The text before it is delivered as a partial line. To keep arbitrary output from being
    // taken for a record, the sentinel must be followed by the rest of the line being a valid base64
    // payload (the cancel marker: nothing at all).
    private OutputReceivedHandler? WrapWithSentinelDetection(OutputReceivedHandler? handler)
    {
        if (handler is null) return null;
        return (data, kind) =>
        {
            if (kind != OutputKind.StandardOutput || !TryFindRecord(data, out int at))
            {
                handler(data, kind);
                return;
            }

            if (at > 0) handler(data[..at], OutputKind.StandardOutputPartial);
            string payload = data[(at + 1)..];
            switch (data[at])
            {
                case RsPrefix:
                    handler(payload, OutputKind.DumpHtml);
                    break;
                case CancelListeningPrefix:
                    _scriptListensForStop = true;
                    break;
                default:
                    var request = ParseInputRequest(payload);
                    if (request is { Path: { } pathName, Read: true })
                    {
                        _ = AnswerPathAsync(pathName); // Util.GetPath: the script gets one path, never the store
                        break;
                    }
                    if (request is { Secret: { } read, Read: true })
                    {
                        _ = AnswerSecretAsync(read); // Util.GetSecret: read here, so the store's key stays in this process
                        break;
                    }
                    if (request is { Secret: { } name } && (request.Value is not null || request.Remove))
                    {
                        _ = KeepSecretAsync(name, request.Value); // Util.SetSecret: nobody is asked
                        break;
                    }
                    _pendingSecret = request.Secret;
                    OnInputRequested(request.Prompt, request.IsPassword);
                    break;
            }
        };
    }

    private bool TryFindRecord(string line, out int index)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            ReadOnlySpan<char> payload = line.AsSpan(i + 1);
            bool isRecord = c switch
            {
                RsPrefix => IsBase64Payload(payload),
                CancelListeningPrefix => payload.IsEmpty,
                InputRequestPrefix => _currentInputStream is not null && IsBase64Payload(payload),
                _ => false,
            };
            if (isRecord)
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    // The payload is trimmed as the consumers do (a stray CR or space must not hide a record).
    private static bool IsBase64Payload(ReadOnlySpan<char> payload)
    {
        payload = payload.Trim();
        return !payload.IsEmpty && Convert.TryFromBase64Chars(payload, new byte[payload.Length], out _);
    }

    // The secret the script is asking for (Util.GetSecret found none), to keep with the answer.
    private string? _pendingSecret;

    // Util.GetSecret: 1 and the value, 0 for none, 2 and why the store can't be read (Base64 after the digit), or 3 when the user
    // didn't let the script read it, on the script's stdin.
    private async Task AnswerSecretAsync(string name)
    {
        if (_currentInputStream is not { } stream) return;
        string answer;
        try
        {
            answer = !await MayAccessAsync(name, isSave: false) ? "3"
                : await Task.Run(() => SecretReader?.Invoke(name)) is { } value
                ? "1" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                : "0";
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<CompiledDotnetLanguageHostBase>().LogWarning(ex, "Could not read the secret '{Name}' from the Secret Store", name);
            answer = "2" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"Invoklet's Secret Store can't be read: {ex.Message}"));
        }
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(answer + "\n"));
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The script ended meanwhile.
        }
    }

    // Whether the user lets this script reach the secret; a failing question is a no.
    private async Task<bool> MayAccessAsync(string name, bool isSave, SecretChange change = SecretChange.None)
    {
        if (SecretAccessApprover is not { } approve) return true;
        try
        {
            var run = _runCts;
            return await approve(new SecretAccess(_options.CurrentQueryPath, name, isSave, change) { RunIdentity = run },
                run?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                _loggerFactory.CreateLogger<CompiledDotnetLanguageHostBase>().LogWarning(ex, "Could not ask whether the script may use the secret '{Name}'", name);
            return false;
        }
    }

    // Util.SetSecret: kept (removed, for null) through SecretSaver, then the script is told 1 (done), 0 (not kept) or 3 (not allowed) on its stdin.
    private async Task KeepSecretAsync(string name, string? value)
    {
        if (_currentInputStream is not { } stream) return;
        var kept = false;
        // A secret already kept is replaced or removed only with the user's say: a script must not wipe what another one needs.
        var change = await Task.Run(() => Exists(name)) ? value is null ? SecretChange.Remove : SecretChange.Replace : SecretChange.None;
        var allowed = await MayAccessAsync(name, isSave: true, change);
        if (allowed && SecretSaver is { } save)
        {
            try
            {
                await Task.Run(() => save(name, value));
                kept = true;
            }
            catch (Exception ex)
            {
                _loggerFactory.CreateLogger<CompiledDotnetLanguageHostBase>().LogWarning(ex, "Could not keep the secret '{Name}' in the Secret Store", name);
            }
        }
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(!allowed ? "3\n" : kept ? "1\n" : "0\n"));
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The script ended meanwhile.
        }
    }

    // Whether the store holds the name; a store that can't be read holds nothing as far as the question goes (the save fails on it anyway).
    private bool Exists(string name)
    {
        try { return SecretReader?.Invoke(name) is not null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }

    // Util.GetPath: 1 and the path, 0 for none, 2 and why the store can't be read, or 4 and why the name isn't valid (Base64 after the
    // digit), on the script's stdin. Reading needs nobody's say: a path is no secret, and the script gets only the one it names.
    private async Task AnswerPathAsync(string name)
    {
        if (_currentInputStream is not { } stream) return;
        string answer;
        try
        {
            var valid = PathStoreRules.Name(name);
            answer = await Task.Run(() => PathReader?.Invoke(valid)) is { } path ? "1" + Base64(path) : "0";
        }
        catch (ArgumentException ex)
        {
            answer = "4" + Base64(ex.Message);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<CompiledDotnetLanguageHostBase>().LogWarning(ex, "Could not read the path '{Name}' from the Path Store", name);
            answer = "2" + Base64($"Invoklet's Path Store can't be read: {ex.Message}");
        }
        await AnswerAsync(stream, answer);
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static async Task AnswerAsync(Stream stream, string answer)
    {
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(answer + "\n"));
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The script ended meanwhile.
        }
    }

    private static (string? Prompt, bool IsPassword, string? Secret, string? Value, bool Remove, bool Read, string? Path) ParseInputRequest(string base64)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var prompt = root.TryGetProperty("Prompt", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
            var isPassword = root.TryGetProperty("IsPassword", out var ip) && ip.ValueKind == JsonValueKind.True;
            var secret = root.TryGetProperty("Secret", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var value = root.TryGetProperty("Value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var remove = root.TryGetProperty("Remove", out var r) && r.ValueKind == JsonValueKind.True;
            var read = root.TryGetProperty("Read", out var rd) && rd.ValueKind == JsonValueKind.True;
            var path = root.TryGetProperty("Path", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() : null;
            return (prompt, isPassword, secret, value, remove, read, path);
        }
        catch (Exception)
        {
            return (null, false, null, null, false, false, null);
        }
    }

    private static void EmitDiagnostics(IReadOnlyList<IDiagnostic> diagnostics, OutputReceivedHandler? handler)
    {
        if (handler is null) return;
        foreach (IDiagnostic d in diagnostics)
        {
            string severity = d.Severity.ToString().ToLowerInvariant();
            // Line 0 = no source position (e.g. "CSC : error CS5001", MSBuild-level errors) — a
            // "(0,0)" prefix would point at a location that doesn't exist.
            string position = d.Line > 0 ? $"({d.Line},{d.Column}): " : "";
            string code = d.Code.Length > 0 ? $" {d.Code}" : "";
            string text = $"{position}{severity}{code}: {d.Message}";
            OutputKind kind = d.Severity == DiagnosticSeverity.Error
                ? OutputKind.StandardError
                : OutputKind.Diagnostic;
            handler(text, kind);
        }
    }

    // Forwards the child's stdout and stderr to the handlers as they arrive: every complete line as
    // one call (without its terminator), and text that ends without a line terminator as a partial
    // line, so Console.Write is not given a newline it never wrote. A partial line is held until its
    // line completes or the stream ends, so a long line split by the pipe is not delivered in pieces;
    // it is delivered earlier only when the other stream has output to deliver first, keeping the
    // order the script produced the two in.
    private sealed class OutputDrain(OutputReceivedHandler? stdout, OutputReceivedHandler? stderr)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _stdoutPending = new();
        private readonly StringBuilder _stderrPending = new();

        public async Task DrainAsync(StreamReader reader, OutputKind kind, CancellationToken ct)
        {
            bool isError = kind == OutputKind.StandardError;
            StringBuilder pending = isError ? _stderrPending : _stdoutPending;
            StringBuilder other = isError ? _stdoutPending : _stderrPending;
            OutputReceivedHandler? handler = isError ? stderr : stdout;
            OutputReceivedHandler? otherHandler = isError ? stdout : stderr;
            OutputKind otherPartial = isError ? OutputKind.StandardOutputPartial : OutputKind.StandardErrorPartial;
            OutputKind partial = isError ? OutputKind.StandardErrorPartial : OutputKind.StandardOutputPartial;

            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                lock (_gate)
                {
                    int scanned = pending.Length; // a pending line has no terminator yet, so only new text is scanned
                    pending.Append(buffer, 0, read);
                    int start = 0;
                    for (int i = scanned; i < pending.Length; i++)
                    {
                        if (pending[i] != '\n') continue;
                        int end = i > start && pending[i - 1] == '\r' ? i - 1 : i;
                        string line = pending.ToString(start, end - start);
                        // A stdout line that already holds the start of a record stays whole: delivered as a
                        // partial line, its base64 would be shown as text, and so would the rest of it.
                        if (!(isError && HoldsRecord(other))) Flush(other, otherHandler, otherPartial);
                        handler?.Invoke(line, kind);
                        start = i + 1;
                    }
                    pending.Remove(0, start);
                }
            }

            lock (_gate)
            {
                Flush(pending, handler, partial);
            }
        }

        private static bool HoldsRecord(StringBuilder pending)
        {
            for (int i = 0; i < pending.Length; i++)
                if (pending[i] is RsPrefix or InputRequestPrefix) return true;
            return false;
        }

        private static void Flush(StringBuilder pending, OutputReceivedHandler? handler, OutputKind kind)
        {
            if (pending.Length == 0) return;
            string text = pending.ToString();
            pending.Clear();
            handler?.Invoke(text, kind);
        }
    }
}
