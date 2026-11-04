namespace Invoklet.Windows;

/// <summary>
/// A window the app opens at startup that needs more than its first layout before it looks ready.
/// The splash screen stays up, and the window hidden, until every one of <see cref="StartupSteps"/> is done.
/// </summary>
internal interface IStartupWindow
{
    /// <summary>What the window waits for, in order; the splash shows the status of the first one not done yet.</summary>
    IReadOnlyList<StartupStep> StartupSteps { get; }
}

/// <summary>One thing a startup window waits for, and what the splash says meanwhile. <see cref="Done"/> never faults.</summary>
internal sealed record StartupStep(string Status, Task Done);
