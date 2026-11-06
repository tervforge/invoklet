namespace Invoklet.UserControls.Models;

/// <summary>What an inactive tab tells about itself in the place of its close button.</summary>
public enum TabStatus { None, Unsaved, Running, Debugging, Paused, Failed }

public static class TabStatuses
{
    /// <summary>The one status to show: a failed run, then a run in progress, then unsaved text.</summary>
    public static TabStatus Of(bool hadRunError, RunState runState, bool isDirty)
    {
        if (hadRunError) return TabStatus.Failed;
        return runState switch
        {
            RunState.Run => TabStatus.Running,
            RunState.RunDebug => TabStatus.Debugging,
            RunState.Paused => TabStatus.Paused,
            _ => isDirty ? TabStatus.Unsaved : TabStatus.None,
        };
    }

    public static string? Describe(TabStatus status) => status switch
    {
        TabStatus.Unsaved => "Unsaved changes",
        TabStatus.Running => "Running",
        TabStatus.Debugging => "Debugging",
        TabStatus.Paused => "Paused in the debugger",
        TabStatus.Failed => "The last run failed",
        _ => null,
    };
}
