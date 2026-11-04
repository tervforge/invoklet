using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.VB;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Windows;
using Invoklet.Windows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TextMateSharp.Grammars;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

// The selected node's text color used to leak into its children: the style matched every TextBlock
// below a selected item, so with ".NET" selected, "C#" and "Visual Basic" turned white on the
// light background.
public class PreferencesWindowTreeTests
{
    private static readonly IBrush Primary = Brushes.Black;
    private static readonly IBrush Accent = Brushes.White;

    [AvaloniaFact]
    public void SelectingAParentNode_Should_ColorOnlyItsOwnText()
    {
        var registry = new LanguageHostRegistry();
        registry.Register(new CSharpLanguageHostFactory(NullLoggerFactory.Instance));
        registry.Register(new VbLanguageHostFactory(NullLoggerFactory.Instance));
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false),
            new EditorTheme("Test", ThemeName.LightPlus, false)));
        theme.Setup(t => t.InterfaceThemes).Returns([]);
        theme.Setup(t => t.EditorThemes).Returns([]);
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Experimental).Returns(new ExperimentalSettings());
        var vm = new PreferencesWindowViewModel(theme.Object, prefs.Object, registry);

        var window = new PreferencesWindow(vm);
        window.Resources["Invoklet.Text.PrimaryBrush"] = Primary;
        window.Resources["Invoklet.Accent.ForegroundBrush"] = Accent;
        window.Resources["Invoklet.Accent.BackgroundBrush"] = Brushes.Blue;
        window.Show();
        try
        {
            foreach (var item in window.GetVisualDescendants().OfType<TreeViewItem>().ToList())
                item.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            foreach (var item in window.GetVisualDescendants().OfType<TreeViewItem>().ToList())
                item.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            vm.SelectedNode = vm.Nodes.Single(n => n.Id == "languages").Children.Single(n => n.Id == "dotnet");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(Accent, TextOf(window, ".NET").Foreground);
            Assert.Same(Primary, TextOf(window, "C#").Foreground);
            Assert.Same(Primary, TextOf(window, "Visual Basic").Foreground);
            Assert.Same(Primary, TextOf(window, "Keyboard").Foreground);
        }
        finally
        {
            window.Close();
        }
    }

    // Searching finds a setting written on a page, not only a node's name: the page is selected and the word is highlighted
    // in its label and in the tree.
    [AvaloniaFact]
    public void Search_Should_FindALabelOnAPage_AndHighlightTheWord()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false),
            new EditorTheme("Test", ThemeName.LightPlus, false)));
        theme.Setup(t => t.InterfaceThemes).Returns([]);
        theme.Setup(t => t.EditorThemes).Returns([]);
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Experimental).Returns(new ExperimentalSettings());
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        var vm = new PreferencesWindowViewModel(theme.Object, prefs.Object, new LanguageHostRegistry());
        var window = new PreferencesWindow(vm);
        window.Show();
        try
        {
            vm.FilterText = "restore open";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("editor", vm.SelectedNode?.Id);
            var label = window.GetVisualDescendants().OfType<CheckBox>()
                .Select(c => c.Content).OfType<TextBlock>()
                .Single(t => Invoklet.UserControls.SearchHighlight.GetText(t) == "Restore open tabs and unsaved changes");
            var match = Assert.Single(label.Inlines!.OfType<global::Avalonia.Controls.Documents.Run>(), r => r.FontWeight == FontWeight.SemiBold);
            Assert.Equal("Restore open", match.Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TreeViewItem>(), item => item.DataContext is SettingsNode { Id: "editor" });
        }
        finally
        {
            window.Close();
        }
    }

    // Not only the names: a description, a tooltip, a choice of a list and a field's value find their page too.
    [AvaloniaTheory]
    [InlineData("takes effect at once", "logging")]       // a description, written in the secondary colour
    [InlineData("log files and console", "logging")]      // a tooltip
    [InlineData("nothing is logged", "logging")]          // a choice of the verbosity list
    [InlineData("IDE0055", "languages.csharp")]           // a value in a field (the excluded diagnostic IDs)
    public void Search_Should_FindDescriptionsChoicesAndValues(string query, string page)
    {
        var settings = new AppSettings();
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Light, false),
            new EditorTheme("Test", ThemeName.LightPlus, false)));
        theme.Setup(t => t.InterfaceThemes).Returns([]);
        theme.Setup(t => t.EditorThemes).Returns([]);
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Keyboard).Returns(settings.Keyboard);
        prefs.Setup(p => p.Experimental).Returns(settings.Experimental);
        prefs.Setup(p => p.Editor).Returns(settings.Editor);
        prefs.Setup(p => p.Session).Returns(settings.Session);
        prefs.Setup(p => p.CSharp).Returns(settings.CSharp);
        prefs.Setup(p => p.Logging).Returns(settings.Logging);
        var registry = new LanguageHostRegistry();
        registry.Register(new CSharpLanguageHostFactory(NullLoggerFactory.Instance));
        var vm = new PreferencesWindowViewModel(theme.Object, prefs.Object, registry);
        var window = new PreferencesWindow(vm);
        window.Show();
        try
        {
            vm.FilterText = query;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(page, vm.SelectedNode?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBlock TextOf(Window window, string text)
        => window.GetVisualDescendants().OfType<TextBlock>().Single(t => (Invoklet.UserControls.SearchHighlight.GetText(t) ?? t.Text) == text);
}
