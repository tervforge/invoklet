namespace Invoklet.Services;

/// <summary>
/// The words for a file operation that failed: what failed and on which file, why in plain words, and the folder on a line of its own.
/// .NET's own messages name the full path again ("Access to the path '/Users/…/a.inv' is denied."), which made one long run of text.
/// </summary>
internal static class FileErrorText
{
    /// <param name="failed">What failed, as a sentence start: "Could not save".</param>
    public static string Describe(string failed, string path, Exception exception)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var folder = Path.GetDirectoryName(trimmed);
        return $"{failed} '{name}'.\n{Reason(exception, path)}" + (string.IsNullOrEmpty(folder) ? "" : "\n\n" + folder);
    }

    /// <summary>Why it failed, without the path the operation was about.</summary>
    public static string Reason(Exception exception, string path) => exception switch
    {
        UnauthorizedAccessException => "Access was denied.",
        FileNotFoundException => "It no longer exists.",
        DirectoryNotFoundException => "Its folder no longer exists.",
        PathTooLongException => "Its path is too long.",
        _ => exception.Message.Replace(path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), StringComparison.Ordinal),
    };
}
