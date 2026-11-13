using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Invoklet.Avalonia.Themes;
using Invoklet.Services.Preferences;
using TextMateSharp.Grammars;

namespace Invoklet.Services;

public delegate void ThemeChangedDelegate(ActiveTheme theme);

public interface IThemeService
{
    IReadOnlyList<InterfaceTheme> InterfaceThemes { get; }
    IReadOnlyList<EditorTheme> EditorThemes { get; }

    ActiveTheme CurrentTheme { get; }

    void SetMode(ThemeMode mode);
    void SetInterfaceTheme(InterfaceTheme theme);
    void SetEditorTheme(EditorTheme theme);

    IBrush? GetBrush(string key);

    event ThemeChangedDelegate? ThemeChanged;
}

public sealed class ThemeService : IThemeService, IDisposable
{
    private static readonly string[] BundledOrder =
    [
        "invoklet.theme.vscode-dark", "invoklet.theme.rider-dark", "invoklet.theme.vs2022-dark", "invoklet.theme.midnight",
        "invoklet.theme.vscode-light", "invoklet.theme.rider-light", "invoklet.theme.vs2022-light", "invoklet.theme.warm-light",
    ];

    // ── Catalog ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<EditorTheme> AllEditorThemes =
    [
        // Dark
        new("Dark+",                ThemeName.DarkPlus,          true),
        new("Monokai",              ThemeName.Monokai,           true),
        new("Abyss",                ThemeName.Abbys,             true),
        new("Kimbie Dark",          ThemeName.KimbieDark,        true),
        new("Tomorrow Night Blue",  ThemeName.TomorrowNightBlue, true),
        new("High Contrast Dark",   ThemeName.HighContrastDark,  true),
        new("Solarized Dark",       ThemeName.SolarizedDark,     true),
        new("Atom One Dark",        ThemeName.AtomOneDark,       true),
        new("Visual Studio Dark",   ThemeName.VisualStudioDark,  true),
        // Light
        new("Light+",               ThemeName.Light,             false),
        new("Quiet Light",          ThemeName.QuietLight,        false),
        new("Solarized Light",      ThemeName.SolarizedLight,    false),
        new("Atom One Light",       ThemeName.AtomOneLight,      false),
        new("Visual Studio Light",  ThemeName.VisualStudioLight, false),
        new("High Contrast Light",  ThemeName.HighContrastLight, false),
    ];

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Application _app;
    private readonly IPreferencesService _prefs;
    private readonly List<ThemeVariant> _installedVariants = [];
    private ResourceDictionary _activeResources;
    private bool _disposed;
    private ActiveTheme _currentTheme;

    public ThemeService(Application app, IPreferencesService prefs, IReadOnlyList<InterfaceTheme>? additionalThemes = null,
        IReadOnlyList<EditorTheme>? additionalEditors = null)
    {
        _app = app;
        _prefs = prefs;
        InterfaceThemes = additionalThemes is { Count: > 0 }
            ? Array.AsReadOnly(BuiltInInterfaceThemes.All.Concat(additionalThemes).OrderBy(theme =>
            {
                var index = Array.IndexOf(BundledOrder, theme.Id);
                return index < 0 ? int.MaxValue : index;
            }).ToArray())
            : BuiltInInterfaceThemes.All;
        EditorThemes = additionalEditors is { Count: > 0 }
            ? Array.AsReadOnly(AllEditorThemes.Concat(additionalEditors).ToArray())
            : AllEditorThemes;

        foreach (var theme in additionalThemes ?? [])
        {
            if (theme.Definition is not { } definition || _app.Resources.ThemeDictionaries.ContainsKey(theme.Variant)) continue;
            _app.Resources.ThemeDictionaries.Add(theme.Variant, ThemeBuilder.BuildPalette(definition));
            _installedVariants.Add(theme.Variant);
        }

        MigrateThemeNames();

        // Synchronous on startup — no UI input events in flight yet, and no ThemeChanged
        // subscribers exist before the constructor returns, so ApplyCore's own notification isn't
        // needed here — just resolve the initial theme directly.
        _currentTheme = ResolveCurrentTheme();
        _activeResources = ThemeBuilder.BuildActiveResources(_currentTheme.Interface.Definition);
        _app.Resources.MergedDictionaries.Add(_activeResources);
        _app.RequestedThemeVariant = _currentTheme.Interface.Variant;
        if (app.PlatformSettings is { } ps)
            ps.ColorValuesChanged += OnColorValuesChanged;
    }

    // ── IThemeService ─────────────────────────────────────────────────────────

    public IReadOnlyList<InterfaceTheme> InterfaceThemes { get; }
    public IReadOnlyList<EditorTheme> EditorThemes { get; }
    public ActiveTheme CurrentTheme => _currentTheme;

    public event ThemeChangedDelegate? ThemeChanged;

    public void SetMode(ThemeMode mode)
    {
        _prefs.Appearance.Mode = mode;
        _prefs.Save();
        ScheduleApply();
    }

    public void SetInterfaceTheme(InterfaceTheme theme)
    {
        if (ResolveIsDark()) _prefs.Appearance.DarkInterfaceTheme = theme.Id;
        else _prefs.Appearance.LightInterfaceTheme = theme.Id;
        _prefs.Save();
        ScheduleApply();
    }

    public void SetEditorTheme(EditorTheme theme)
    {
        if (ResolveIsDark()) _prefs.Appearance.DarkEditorTheme = theme.Id;
        else _prefs.Appearance.LightEditorTheme = theme.Id;
        _prefs.Save();
        ScheduleApply();
    }

    public IBrush? GetBrush(string key)
    {
        object? val = null;
        _app.TryGetResource(key, _app.ActualThemeVariant, out val);
        return val as IBrush;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_app.PlatformSettings is { } ps)
            ps.ColorValuesChanged -= OnColorValuesChanged;
        _app.Resources.MergedDictionaries.Remove(_activeResources);
        foreach (var variant in _installedVariants) _app.Resources.ThemeDictionaries.Remove(variant);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    // Post entire apply at Background priority (= 4).
    // All pointer/keyboard input events process at Input priority (= 5) which is HIGHER,
    // so they complete fully before we touch RequestedThemeVariant or fire ThemeChanged.
    // This eliminates "PlatformImpl is null" errors from InputManager.
    private void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (_prefs.Appearance.Mode == ThemeMode.System)
            _app.Dispatcher.Post(ApplyCore, DispatcherPriority.Background);
    }

    // The application's own dispatcher, not the static Dispatcher.UIThread: a change can come from another thread, where reading that
    // static while it is unset (between isolated tests) would make that thread the UI thread.
    private void ScheduleApply()
        => _app.Dispatcher.Post(ApplyCore, DispatcherPriority.Background);

    private void ApplyCore()
    {
        if (_disposed) return;
        var theme = ResolveCurrentTheme();
        if (theme.Interface != _currentTheme.Interface)
        {
            var dictionaries = _app.Resources.MergedDictionaries;
            var resources = ThemeBuilder.BuildActiveResources(theme.Interface.Definition);
            var index = dictionaries.IndexOf(_activeResources);
            if (index >= 0) dictionaries[index] = resources;
            else dictionaries.Add(resources);
            _activeResources = resources;
        }

        _currentTheme = theme;
        _app.RequestedThemeVariant = _currentTheme.Interface.Variant;
        ThemeChanged?.Invoke(_currentTheme);
    }

    private ActiveTheme ResolveCurrentTheme()
    {
        bool isDark = ResolveIsDark();
        var a = _prefs.Appearance;

        var interfaceTheme = ResolveInterface(isDark ? a.DarkInterfaceTheme : a.LightInterfaceTheme, isDark);
        var editorTheme = ResolveEditor(isDark ? a.DarkEditorTheme : a.LightEditorTheme, interfaceTheme);
        return new ActiveTheme(interfaceTheme, editorTheme);
    }

    private bool ResolveIsDark() => _prefs.Appearance.Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => _app.PlatformSettings?.GetColorValues().ThemeVariant != PlatformThemeVariant.Light,
    };

    private InterfaceTheme ResolveInterface(string id, bool isDark)
        => InterfaceThemes.FirstOrDefault(t => t.IsDark == isDark && t.Id == id)
           ?? InterfaceThemes.First(t => t.IsDark == isDark);

    private EditorTheme ResolveEditor(string? id, InterfaceTheme theme)
    {
        var editorId = id;
        if (string.IsNullOrEmpty(editorId) && theme.Definition is { EditorDefault: { } requested } definition)
            editorId = definition.EditorThemes.TryGetValue(requested, out var asset)
                ? ThemeCatalog.EditorId(asset.ThemeId, requested)
                : requested;

        return EditorThemes.FirstOrDefault(t => t.IsDark == theme.IsDark && t.Id == editorId)
            ?? AllEditorThemes.First(t => t.IsDark == theme.IsDark);
    }

    private void MigrateThemeNames()
    {
        var settings = _prefs.Appearance;
        var darkInterface = InterfaceId(settings.DarkInterfaceTheme, true);
        var lightInterface = InterfaceId(settings.LightInterfaceTheme, false);
        var darkEditor = EditorId(settings.DarkEditorTheme, true);
        var lightEditor = EditorId(settings.LightEditorTheme, false);
        if (darkInterface == settings.DarkInterfaceTheme && lightInterface == settings.LightInterfaceTheme
            && darkEditor == settings.DarkEditorTheme && lightEditor == settings.LightEditorTheme) return;

        settings.DarkInterfaceTheme = darkInterface;
        settings.LightInterfaceTheme = lightInterface;
        settings.DarkEditorTheme = darkEditor;
        settings.LightEditorTheme = lightEditor;
        _prefs.Save();

        string InterfaceId(string value, bool dark) =>
            InterfaceThemes.FirstOrDefault(t => t.IsDark == dark && t.Name == value)?.Id ?? value switch
            {
                "VS Code Dark" => "invoklet.theme.vscode-dark",
                "Rider Dark" => "invoklet.theme.rider-dark",
                "VS 2022 Dark" => "invoklet.theme.vs2022-dark",
                "VS Code Light" => "invoklet.theme.vscode-light",
                "Rider Light" => "invoklet.theme.rider-light",
                "VS 2022 Light" => "invoklet.theme.vs2022-light",
                _ => value,
            };

        string? EditorId(string? value, bool dark) =>
            EditorThemes.FirstOrDefault(t => t.IsDark == dark && t.Name == value)?.Id ?? value;
    }
}
