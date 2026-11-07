namespace Invoklet.Services;

/// <summary>Which window owns a dialog, from a snapshot of the windows: the last one that is active and shown, else the main window.</summary>
internal static class DialogOwner
{
    internal readonly record struct Candidate<T>(T Window, bool IsActive, bool IsVisible);

    internal static T? Choose<T>(IReadOnlyList<Candidate<T>> windows, T? main) where T : class =>
        windows.LastOrDefault(w => w.IsActive && w.IsVisible).Window ?? main;
}
