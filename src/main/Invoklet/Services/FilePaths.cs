namespace Invoklet.Services;

/// <summary>How the paths of files are compared on this system: not by case on Windows and macOS, by case on Linux, where <c>a.inv</c> and <c>A.inv</c> are two files.</summary>
internal static class FilePaths
{
    private static readonly bool IgnoresCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparer Comparer { get; } = IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The same rule as <see cref="Comparer"/>, for what takes a <see cref="StringComparison"/> (a path that starts with a folder's).</summary>
    public static StringComparison Comparison { get; } = IgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
