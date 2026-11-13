using Invoklet.Avalonia.Themes;
using Invoklet.Services.Scaling;

namespace Invoklet.Services.Preferences;

public sealed class AppearanceSettings
{
    public bool RestoreUnsavedTabs { get; set; } = true;
    public ThemeMode Mode { get; set; } = ThemeMode.System;
    public string DarkInterfaceTheme { get; set; } = "invoklet.theme.midnight";
    public string LightInterfaceTheme { get; set; } = "invoklet.theme.warm-light";
    public string? DarkEditorTheme { get; set; }
    public string? LightEditorTheme { get; set; }

    /// <summary>
    /// Size of the whole interface, code included, in percent (100 = as designed). Missing in older
    /// settings files, which therefore load at 100%.
    /// </summary>
    public double InterfaceScalePercent
    {
        get;
        set => field = ScalePercent.NormalizeInterface(value);
    } = ScalePercent.Default;
}
