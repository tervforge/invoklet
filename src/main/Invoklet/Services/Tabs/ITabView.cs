namespace Invoklet.Services.Tabs;

/// <summary>What the lifecycle manager needs from the view of a tab.</summary>
internal interface ITabView
{
    /// <summary>A script of the tab is running or being debugged.</summary>
    bool HasRunningProcess { get; }

    /// <summary>The text and the output are on disk, not in the view.</summary>
    bool IsContentReleased { get; }

    /// <summary>
    /// Moves the text and the output to disk without holding up the UI: what is to be kept is taken at once, written elsewhere, and the
    /// tab lets go of it only if <paramref name="stillWanted"/> still says so then and the text has not been edited meanwhile. False: nothing
    /// was released, and the tab still has its text (the write failed, or the tab was needed again).
    /// </summary>
    Task<bool> TryReleaseContentAsync(Func<bool> stillWanted);

    /// <summary>Brings them back, without holding up the UI. False: they could not be read, and the tab stays released.</summary>
    Task<bool> TryRestoreContentAsync();

    /// <summary>Only the view being looked at colours its text.</summary>
    void SetHighlightingActive(bool active);
}
