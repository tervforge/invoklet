using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Invoklet.Services.Preferences;

public sealed class PreferencesService : IPreferencesService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Invoklet", "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<PreferencesService> _logger;
    private AppSettings _settings = new();

    public AppearanceSettings Appearance => _settings.Appearance;
    public EditorSettings Editor => _settings.Editor;
    public DebuggerSettings Debugger => _settings.Debugger;
    public CSharpLanguageSettings CSharp => _settings.CSharp;
    public KeyboardSettings Keyboard => _settings.Keyboard;
    public LayoutSettings Layout => _settings.Layout;
    public SessionSettings Session => _settings.Session;
    public ExperimentalSettings Experimental => _settings.Experimental;
    public LoggingSettings Logging => _settings.Logging;
    public Dictionary<string, bool> System => _settings.System;

    public event Action? Changed;

    public PreferencesService(ILogger<PreferencesService> logger)
    {
        _logger = logger;
        Load();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_settings, SerializerOptions));
        File.Move(tmp, SettingsPath, overwrite: true);
        Changed?.Invoke();
    }

    /// <summary>
    /// The settings as saved, for choices made before Avalonia (and this service) start;
    /// defaults when the file is missing or unreadable.
    /// </summary>
    internal static AppSettings ReadForStartup()
    {
        try
        {
            return File.Exists(SettingsPath) ? AppSettings.Parse(File.ReadAllText(SettingsPath)) : new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    private void Load()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            // The settings are taken over only when the whole file has been read and migrated; a failure leaves the defaults.
            var loaded = AppSettings.Parse(File.ReadAllText(SettingsPath));
            var migrated = loaded.Keyboard.MigrateLegacy();
            _settings = loaded;
            if (migrated) Save();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings file is corrupt; starting with defaults. Path: {Path}", SettingsPath);
            KeepCopyOfUnreadableFile();
        }
    }

    // The next save writes the defaults over the file; what the user had in it stays in a copy next to it.
    private void KeepCopyOfUnreadableFile()
    {
        try
        {
            File.Copy(SettingsPath, SettingsPath + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not keep a copy of the unreadable settings file");
        }
    }
}
