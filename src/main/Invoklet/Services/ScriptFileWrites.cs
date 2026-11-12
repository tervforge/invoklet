namespace Invoklet.Services;

/// <summary>
/// An in-place save of a script whose editor has a known baseline. Content-based permissions can follow edits from that baseline.
/// </summary>
internal static class ScriptFileWrites
{
    /// <summary>The full path, baseline fingerprint and saved fingerprint; raised on the thread that wrote.</summary>
    public static event Action<string, string, string>? Written;

    internal static void Report(string path, byte[] baselineHash, byte[] savedHash) =>
        Written?.Invoke(Path.GetFullPath(path), Convert.ToHexString(baselineHash), Convert.ToHexString(savedHash));
}
