using Invoklet.Contracts.Debugging;
using Invoklet.Contracts.Debugging.Delegates;
using Invoklet.Contracts.Models;

namespace Invoklet.Contracts;

public abstract class LanguageHostBase : ILanguageHost
{
    private readonly List<IBreakpoint> _breakpoints = new();
    private BreakpointHitHandler? _breakpointHit;
    private BreakpointVerifiedHandler? _breakpointVerified;
    private CompiledHandler? _compiled;
    private InputRequestedHandler? _inputRequested;
    private StageChangedHandler? _stageChanged;
    private bool _disposed;

    public abstract string Language { get; }
    public abstract LanguageHostCapabilities Capabilities { get; }

    public bool Supports(LanguageHostCapabilities capability) => Capabilities.HasFlag(capability);

    // --- Core: must be implemented by every host ---

    public abstract Task<ICompilationResult> CompileAsync(string code, CancellationToken cancellationToken);

    public virtual event CompiledHandler? Compiled
    {
        add => _compiled += value;
        remove => _compiled -= value;
    }

    public virtual event StageChangedHandler? StageChanged
    {
        add => _stageChanged += value;
        remove => _stageChanged -= value;
    }

    public abstract Task<RunResult> RunAsync(
        string code,
        OutputReceivedHandler outputHandler,
        CancellationToken cancellationToken);

    public abstract Task TerminateAsync(CancellationToken cancellationToken);

    public virtual event InputRequestedHandler? InputRequested
    {
        add => _inputRequested += value;
        remove => _inputRequested -= value;
    }

    public abstract Task SendInputAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a secret the script asked the user for (<c>Util.GetSecret</c> with no such secret yet),
    /// by name and value, before the answer reaches the script, and what <c>Util.SetSecret</c> saves; a null value
    /// (<c>Util.SetSecret(name, null)</c>) removes the secret. Null: answers aren't kept.
    /// </summary>
    public Action<string, string?>? SecretSaver { get; set; }

    /// <summary>
    /// Reads a secret a running script asks for (<c>Util.GetSecret</c>), by name in any letter case; null when the store has none. It throws
    /// <see cref="InvalidDataException"/> (or another <see cref="IOException"/>) when the store can't be read. The script gets only that
    /// value: the store's key stays in this process, and the system keychain is opened only when a script asks. Null: every secret is missing.
    /// </summary>
    public Func<string, string?>? SecretReader { get; set; }

    /// <summary>
    /// Asked before a running script reads a secret (<c>Util.GetSecret</c>) or saves one (<c>Util.SetSecret</c>): whether the script may.
    /// Given the script's file (null for one not saved), the secret's name and whether it is a save. A script that may not gets
    /// <see cref="UnauthorizedAccessException"/>. Null: every script may.
    /// </summary>
    public Func<SecretAccess, CancellationToken, Task<bool>>? SecretAccessApprover { get; set; }

    /// <summary>
    /// Reads a named path a running script asks for (<c>Util.GetPath</c>), by a valid name in any letter case; null when the store has none.
    /// It throws <see cref="IOException"/> (<see cref="InvalidDataException"/> among them) when the store can't be read. The script gets only
    /// that path, never the store. Null: every path is missing.
    /// </summary>
    public Func<string, string?>? PathReader { get; set; }

    // --- Debug: virtual, throws if capability not declared ---

    public virtual Task RunDebugAsync(
        string code,
        OutputReceivedHandler outputHandler,
        CancellationToken cancellationToken)
    {
        EnsureCapability(LanguageHostCapabilities.Debug);
        // A host that declares the Debug capability but doesn't override this is a genuine
        // implementation bug in that host, not a caller mistake (EnsureCapability above already
        // guards the latter) — silently "succeeding" with a completed task that ran nothing would
        // hide it instead of failing loudly.
        throw new NotImplementedException(
            $"Language host '{Language}' declares Debug capability but does not implement {nameof(RunDebugAsync)}.");
    }

    public virtual Task PauseAsync(CancellationToken cancellationToken)
    {
        EnsureCapability(LanguageHostCapabilities.Debug);
        throw new NotImplementedException(
            $"Language host '{Language}' declares Debug capability but does not implement {nameof(PauseAsync)}.");
    }

    // --- Breakpoints: virtual, maintains internal list ---

    public virtual void AddBreakpoint(IBreakpoint breakpoint)
    {
        EnsureCapability(LanguageHostCapabilities.Breakpoints);
        _breakpoints.Add(breakpoint);
    }

    public virtual void RemoveBreakpoint(IBreakpoint breakpoint)
    {
        EnsureCapability(LanguageHostCapabilities.Breakpoints);
        // Match by Line/FileName rather than reference/instance equality: a breakpoint removed from
        // the editor gutter during an active debug session is a freshly-constructed IBreakpoint, not
        // the same instance originally passed to AddBreakpoint.
        _breakpoints.RemoveAll(bp => bp.Line == breakpoint.Line
                                     && (string.IsNullOrEmpty(bp.FileName) || string.IsNullOrEmpty(breakpoint.FileName)
                                                                           || string.Equals(bp.FileName,
                                                                               breakpoint.FileName,
                                                                               StringComparison.OrdinalIgnoreCase)));
    }

    public virtual void ClearBreakpoints()
    {
        EnsureCapability(LanguageHostCapabilities.Breakpoints);
        _breakpoints.Clear();
    }

    public virtual event BreakpointHitHandler? BreakpointHit
    {
        add => _breakpointHit += value;
        remove => _breakpointHit -= value;
    }

    public virtual event BreakpointVerifiedHandler? BreakpointVerified
    {
        add => _breakpointVerified += value;
        remove => _breakpointVerified -= value;
    }

    public virtual string? SourceFilePath => null;

    // --- Memory dump ---

    public virtual Task<IMemoryDump> GetMemoryDumpAsync(CancellationToken cancellationToken)
    {
        EnsureCapability(LanguageHostCapabilities.MemoryDump);
        // See RunDebugAsync's identical reasoning — returning null! here would
        // just relocate the failure to whatever the caller does with the result instead of failing
        // at the actual bug site.
        throw new NotImplementedException(
            $"Language host '{Language}' declares MemoryDump capability but does not implement {nameof(GetMemoryDumpAsync)}.");
    }

    // --- Helpers for subclasses ---

    protected IReadOnlyList<IBreakpoint> ActiveBreakpoints => _breakpoints.AsReadOnly();

    protected void OnBreakpointHit(IBreakpointHitContext context) => _breakpointHit?.Invoke(context);

    protected void OnBreakpointVerified(IBreakpoint breakpoint, bool verified, string? message)
        => _breakpointVerified?.Invoke(breakpoint, verified, message);

    protected void OnStageChanged(RunStage stage, string? detail = null) => _stageChanged?.Invoke(stage, detail);

    protected void OnCompiled(string binaryPath) => _compiled?.Invoke(binaryPath);

    protected void OnInputRequested(string? prompt, bool isPassword) => _inputRequested?.Invoke(prompt, isPassword);

    protected void EnsureCapability(LanguageHostCapabilities required)
    {
        if (!Supports(required))
            throw new NotSupportedException(
                $"Language host '{Language}' does not support '{required}'.");
    }

    protected void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
    }

    // --- Disposal ---

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await TerminateAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
