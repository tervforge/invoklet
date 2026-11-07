using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Avalonia.CodeTextEditor.Breakpoints;
using Invoklet.Contracts;
using Invoklet.UserControls.Models;
using HostBreakpoint = Invoklet.Contracts.Debugging.IBreakpoint;

namespace Invoklet.UserControls;

/// <summary>
/// Keeps the breakpoints of the editor's gutter and those of the running debug session the same: an edit in the gutter or in the
/// options dialog reaches the session at once (without one it is read at the next run's start), and what the debugger says about a
/// breakpoint colours it in the gutter.
/// </summary>
internal sealed class EditorBreakpoints(CodeTextEditor editor, Func<ILanguageHost?> runningHost, Func<string?> sourceFilePath)
{
    // Set while a breakpoint is added with its options, which are sent once they are all set.
    private bool _addingWithOptions;

    /// <summary>
    /// The enabled breakpoints a run hands its debugger; disabled ones stay in the gutter but never reach it. Every breakpoint is
    /// pending again, so the colour of the previous run does not linger until the new session answers.
    /// </summary>
    public IReadOnlyList<(int Line, string? Condition, string? HitCondition)> StartRun()
    {
        var enabled = BreakpointHostBridge.Enabled(editor.Breakpoints);
        foreach (var breakpoint in editor.Breakpoints)
            breakpoint.VerificationState = BreakpointVerificationState.Pending;
        editor.InvalidateBreakpoints();
        return enabled;
    }

    public void OnAdded(object? sender, BreakpointEventArgs e)
    {
        if (_addingWithOptions || !TryGetSession(out var host, out var path)) return;
        var breakpoint = At(e.Line);
        if (breakpoint?.IsEnabled != false)
            BreakpointHostBridge.Add(host, path, e.Line, breakpoint?.Condition, breakpoint?.HitCondition);
    }

    public void OnRemoved(object? sender, BreakpointEventArgs e)
    {
        if (TryGetSession(out var host, out var path)) BreakpointHostBridge.Remove(host, path, e.Line);
    }

    /// <summary>The debugger's answer for a breakpoint, matched to the gutter's by line.</summary>
    public void Verified(HostBreakpoint reported, bool verified, string? message)
    {
        if (At(reported.Line) is not { } breakpoint) return;
        breakpoint.VerificationState = verified ? BreakpointVerificationState.Verified : BreakpointVerificationState.Unverified;
        breakpoint.VerificationMessage = message;
        editor.InvalidateBreakpoints();
    }

    /// <summary>Sets the options of the breakpoint on a line, adding one if there is none. A blank condition is none.</summary>
    public void Apply(int line, string? condition, string? hitCondition, bool enabled)
    {
        condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        hitCondition = string.IsNullOrWhiteSpace(hitCondition) ? null : hitCondition.Trim();
        var breakpoint = At(line);
        if (breakpoint is null)
        {
            _addingWithOptions = true;
            try { editor.AddBreakpoint(line); }
            finally { _addingWithOptions = false; }
            breakpoint = At(line);
        }
        if (breakpoint is null) return;
        breakpoint.Condition = condition;
        breakpoint.HitCondition = hitCondition;
        breakpoint.IsEnabled = enabled;
        editor.InvalidateBreakpoints();
        if (TryGetSession(out var host, out var path))
        {
            BreakpointHostBridge.Remove(host, path, line);
            if (enabled) BreakpointHostBridge.Add(host, path, line, condition, hitCondition);
        }
    }

    private IBreakpoint? At(int line) => editor.Breakpoints.FirstOrDefault(b => b.Line == line);

    private bool TryGetSession(out ILanguageHost host, out string path)
    {
        host = runningHost()!;
        path = sourceFilePath()!;
        return host is not null && path is not null && host.Supports(LanguageHostCapabilities.Breakpoints);
    }
}
