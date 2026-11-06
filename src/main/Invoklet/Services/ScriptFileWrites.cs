namespace Invoklet.Services;

/// <summary>
/// Invoklet writing a script's file itself (a save from its tab). What trusts a file by its contents, such as a script allowed to use the
/// secrets, follows Invoklet's own writes and so notices only another program's.
/// </summary>
internal static class ScriptFileWrites
{
    /// <summary>The full path written and the bytes now in it; raised on the thread that wrote.</summary>
    public static event Action<string, byte[]>? Written;

    internal static void Report(string path, byte[] bytes) => Written?.Invoke(Path.GetFullPath(path), bytes);
}
