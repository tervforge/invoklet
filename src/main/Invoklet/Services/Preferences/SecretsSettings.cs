namespace Invoklet.Services.Preferences;

/// <summary>Which scripts may use the Secret Store without being asked first.</summary>
public sealed class SecretsSettings
{
    /// <summary>The full paths of the script files the user let read and change secrets ("Always Allow").</summary>
    public List<string> AllowedScripts { get; set; } = [];
}
