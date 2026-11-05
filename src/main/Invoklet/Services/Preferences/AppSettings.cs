using System.Text.Json;

namespace Invoklet.Services.Preferences;

public sealed class AppSettings
{
    public AppearanceSettings Appearance { get; set; } = new();
    public EditorSettings Editor { get; set; } = new();
    public DebuggerSettings Debugger { get; set; } = new();
    public CSharpLanguageSettings CSharp { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public SessionSettings Session { get; set; } = new();
    public ExperimentalSettings Experimental { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public SecretsSettings Secrets { get; set; } = new();

    /// <summary>
    /// Settings the OS's desktop integration offers, by id ("linux.nativeWayland"); a missing one has
    /// its default. Kept as plain values so a settings file moved to another OS keeps them unread.
    /// </summary>
    public Dictionary<string, bool> System { get; set; } = [];

    /// <summary>The settings in a file: what it lacks, and what it holds as null, is as in a new one. Throws (JsonException) for a file that is not JSON.</summary>
    internal static AppSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        SettingsDefaults.FillNulls(settings);
        return settings;
    }
}
