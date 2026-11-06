namespace Invoklet.Services.Preferences;

/// <summary>Which scripts may use the Secret Store without being asked first.</summary>
public sealed class SecretsSettings
{
    /// <summary>The full paths of the script files the user let read and change secrets ("Always Allow").</summary>
    public List<string> AllowedScripts { get; set; } = [];

    /// <summary>
    /// The SHA-256 (hex) of each allowed file as it was allowed or last saved by Invoklet, by its path: a file another program changed
    /// (a new download, a replaced copy) is asked about again.
    /// </summary>
    public Dictionary<string, string> AllowedScriptHashes { get; set; } = [];
}
