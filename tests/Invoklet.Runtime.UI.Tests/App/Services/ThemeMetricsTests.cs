using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Resources;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Services.QuickOpen;
using Invoklet.TemplatedControls;
using Invoklet.Windows;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Services;

public sealed class ThemeMetricsTests
{
    [AvaloniaFact]
    public void SetInterfaceTheme_OpenDialogAndMenu_ApplyAndRestoreTheirCornerRadii()
    {
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var themeStyles = TestInterfaceThemes.CreateStyle();
        var popupStyles = new PopupStyles();
        app.Styles.Add(themeStyles);
        app.Styles.Add(popupStyles);
        var (square, plain) = CreateThemes();
        app.Resources.ThemeDictionaries.Add(square.Variant, ThemeBuilder.BuildPalette(square.Definition!));
        app.Resources.ThemeDictionaries.Add(plain.Variant, ThemeBuilder.BuildPalette(plain.Definition!));
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Appearance).Returns(new AppearanceSettings { Mode = ThemeMode.Dark, DarkInterfaceTheme = plain.Name });
        var service = new ThemeService(app, prefs.Object, [square, plain]);
        var dialog = new QuickOpenDialog(new QuickOpenContent([], _ => Task.FromResult<IReadOnlyList<QuickOpenEntry>>([]),
            (_, _) => Task.FromResult<string?>(null), "Ctrl+,"));
        var button = dialog.FindControl<Button>("OpenButton")!;
        var item = new MenuItem { Header = "Action" };
        var menu = new ContextMenu { Items = { item } };
        button.ContextMenu = menu;

        try
        {
            dialog.Show();
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            AssertRadii(new CornerRadius(4), new CornerRadius(8));

            service.SetInterfaceTheme(square);
            Dispatcher.UIThread.RunJobs();
            AssertRadii(default, default);

            service.SetInterfaceTheme(plain);
            Dispatcher.UIThread.RunJobs();
            AssertRadii(new CornerRadius(4), new CornerRadius(8));
        }
        finally
        {
            menu.Close();
            dialog.Close();
            service.Dispose();
            app.RequestedThemeVariant = previousVariant;
            app.Resources.ThemeDictionaries.Remove(square.Variant);
            app.Resources.ThemeDictionaries.Remove(plain.Variant);
            app.Styles.Remove(popupStyles);
            app.Styles.Remove(themeStyles);
        }

        void AssertRadii(CornerRadius control, CornerRadius overlay)
        {
            Assert.True(app.TryGetResource("ControlCornerRadius", app.ActualThemeVariant, out var value));
            Assert.Equal(control, value);
            Assert.Equal(control, button.CornerRadius);
            var row = item.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
            Assert.Equal(control, row.CornerRadius);
            Assert.Equal(overlay, menu.CornerRadius);
        }
    }

    private static (InterfaceTheme Square, InterfaceTheme Plain) CreateThemes()
    {
        var reader = new ThemeDefinitionReader();
        var square = reader.Read("""
            {
              "id":"test.square","name":"Square",
              "metrics":{"Invoklet.Radius.Control":0,"Invoklet.Radius.Overlay":0},
              "icons":{"Icon.Play":"M0,0 L10,0 L10,10 Z","AsmIcon.Type":"M0,0 L5,0 L5,5 Z"}
            }
            """).Definition!;
        var plain = reader.Read("""{"id":"test.plain","name":"Plain"}""").Definition!;
        var dark = TestInterfaceThemes.All.First(theme => theme.IsDark).Definition!.Resources;
        var resolver = new ThemeResolver(new ThemeDefaults(dark, dark, ImmutableDictionary<string, object>.Empty));
        var parents = ImmutableDictionary<string, ThemeDefinition>.Empty;
        return (ThemeBuilder.Describe(resolver.Resolve(square, parents).Theme!),
            ThemeBuilder.Describe(resolver.Resolve(plain, parents).Theme!));
    }

    [AvaloniaFact]
    public void SetInterfaceTheme_OpenWindow_UpdatesIconsAndRestoresBuiltInGeometry()
    {
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var (custom, plain) = CreateThemes();
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Appearance).Returns(new AppearanceSettings { Mode = ThemeMode.Dark, DarkInterfaceTheme = plain.Name });
        using var service = new ThemeService(app, prefs.Object, [custom, plain]);
        var play = new PathIcon();
        var node = new PathIcon();
        play[!PathIcon.DataProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Icon.Play");
        ThemeIcon.SetKey(node, "AsmIcon.Type");
        var window = new Window { Content = new StackPanel { Children = { play, node } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var builtInPlay = Assert.IsAssignableFrom<Geometry>(play.Data);
            var builtInNode = Assert.IsAssignableFrom<Geometry>(node.Data);

            service.SetInterfaceTheme(custom);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(custom.Definition!.Resources["Icon.Play"], play.Data);
            Assert.Same(custom.Definition.Resources["AsmIcon.Type"], node.Data);

            service.SetInterfaceTheme(plain);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(builtInPlay, play.Data);
            Assert.Same(builtInNode, node.Data);
            ThemeIcon.SetKey(node, "Icon.Play");
            Assert.Same(builtInPlay, node.Data);
            ThemeIcon.SetKey(node, null);
            Assert.Null(node.Data);
        }
        finally
        {
            window.Close();
            app.RequestedThemeVariant = previousVariant;
        }
    }
}
