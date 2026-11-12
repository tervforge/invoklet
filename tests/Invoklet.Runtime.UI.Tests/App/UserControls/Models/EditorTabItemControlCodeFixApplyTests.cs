using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Avalonia.CodeTextEditor.CodeFix;
using Invoklet.Avalonia.Themes;
using Invoklet.Contracts;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Invoklet.Services;
using Invoklet.Services.BuildCache;
using Invoklet.Services.Preferences;
using Invoklet.UserControls;
using Invoklet.UserControls.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Models;

// End-to-end coverage of the
// deferred-materialization click flow: a REAL CSharpIntelliSenseService (not mocked — the point is
// whether the whole RoslynCodeFixEngine cache + EditorTabItemControl.ApplyCodeFixAsync pipeline
// actually applies the right result when a specific lightbulb entry is clicked, not whether some mock
// was configured correctly) driving a real EditorsTabItemControl, clicking through the actual
// CodeFixMargin menu-item wiring (same idiom as CodeFixMarginTests.cs's own click test) rather than
// simulating the click by calling internal methods directly. The "add using" fix this test drives
// lands in FrontMatter.Namespaces, not the visible editor text — every Invoklet tab (even a brand-new
// untitled one, see NewMdRunFile) is a real .inv document, and usings are project-level metadata
// there (docs/ImplementationPlanRPad.md), not text inserted into the script itself.
[Trait("Category", "Integration")]
[Trait("Category", "Roslyn")]
public class EditorTabItemControlCodeFixApplyTests
{
    [AvaloniaFact]
    public async Task ClickingFixAllInScript_Should_ApplyBothAccessibilityFixes()
    {
        var prefs = MakePrefs();
        prefs.Object.CSharp.AccessibilityModifiersStyle = "always";
        var vm = new EditorTabItemViewModel(null, [], prefs.Object, new Mock<IEditorTabActions>().Object);
        using var intelliSense = CreateRealIntelliSense();
        using var ctrl = new EditorsTabItemControl(vm, MakeTheme().Object, new Mock<IWindowService>().Object,
            new Mock<ILanguageHostRegistry>().Object, prefs.Object, intelliSense,
            new Mock<IBuildCacheService>().Object, timings: NoPauses.Timings);
        var host = new Window { Content = ctrl };
        host.Show();
        var editor = ctrl.State.Editor;
        var margin = editor.TextArea.LeftMargins.OfType<CodeFixMargin>().Single();
        editor.Text = "class First { }\nclass Second { }";
        CodeFixEntry? FirstFix() => margin.Fixes.FirstOrDefault(f => f.Line == 1 && f.DiagnosticId == "IDE0040");
        await TestWait.WhileAsync(() => FirstFix() is null);
        var fix = FirstFix();
        Assert.NotNull(fix);
        var group = Assert.IsType<MenuItem>(Assert.Single(margin.BuildMenuItems([fix])));
        var apply = Assert.Single(group.Items.OfType<MenuItem>(), item => Equals(item.Header, "Fix all in this script"));

        apply.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await TestWait.EventuallyAsync(() =>
        {
            Assert.Contains("internal class First", editor.Text);
            Assert.Contains("internal class Second", editor.Text);
        });
    }

    [AvaloniaFact]
    public async Task SuppressInThisScript_Should_AlsoSuppressWarningsAboveTheSelectedLine()
    {
        var prefs = MakePrefs();
        var vm = new EditorTabItemViewModel(null, [], prefs.Object, new Mock<IEditorTabActions>().Object);
        using var intelliSense = CreateRealIntelliSense();
        using var ctrl = new EditorsTabItemControl(vm, MakeTheme().Object, new Mock<IWindowService>().Object,
            new Mock<ILanguageHostRegistry>().Object, prefs.Object, intelliSense,
            new Mock<IBuildCacheService>().Object, timings: NoPauses.Timings);
        var host = new Window { Content = ctrl };
        host.Show();
        var editor = ctrl.State.Editor;
        var margin = editor.TextArea.LeftMargins.OfType<CodeFixMargin>().Single();
        const string code = "int first = 1;\nint second = 2;";
        editor.Text = code;
        CodeFixEntry? SecondWarning() => margin.Fixes.FirstOrDefault(f => f.Line == 2 && f.DiagnosticId == "CS0219");
        await TestWait.WhileAsync(() => SecondWarning() is null);
        var fix = SecondWarning();
        Assert.NotNull(fix);
        Assert.Contains(margin.Fixes, f => f.Line == 1 && f.DiagnosticId == "CS0219");
        var group = Assert.IsType<MenuItem>(Assert.Single(margin.BuildMenuItems([fix])));
        var suppress = Assert.Single(group.Items.OfType<MenuItem>(), item => Equals(item.Header, "Suppress in this script"));

        suppress.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("#pragma warning disable CS0219\n" + code, editor.Text);
        await TestWait.EventuallyAsync(() => Assert.DoesNotContain(margin.Fixes, f => f.DiagnosticId == "CS0219"));
    }

    private static Mock<IPreferencesService> MakePrefs()
    {
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.Editor).Returns(new EditorSettings());
        prefs.Setup(p => p.CSharp).Returns(new CSharpLanguageSettings());
        prefs.Setup(p => p.Keyboard).Returns(new KeyboardSettings());
        prefs.Setup(p => p.Layout).Returns(new LayoutSettings());
        prefs.Setup(p => p.Session).Returns(new SessionSettings());
        return prefs;
    }

    private static Mock<IThemeService> MakeTheme()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentTheme).Returns(new ActiveTheme(
            new InterfaceTheme("Test", ThemeVariant.Dark, true),
            new EditorTheme("Test", TextMateSharp.Grammars.ThemeName.DarkPlus, true)));
        return theme;
    }

    private static CSharpIntelliSenseService CreateRealIntelliSense()
    {
        var referenceService = new CSharpReferenceService();
        var refs = referenceService.GetDefault().Append(referenceService.GetExtensionReference());
        return new CSharpIntelliSenseService(refs, NullLogger<CSharpIntelliSenseService>.Instance);
    }

    [AvaloniaFact]
    public async Task ClickingALightbulbEntry_Should_MaterializeAndApplyItsFix_ToTheDocument()
    {
        var prefs = MakePrefs();
        var vm = new EditorTabItemViewModel(
            null, [], prefs.Object, new Mock<IEditorTabActions>().Object);
        var registry = new Mock<ILanguageHostRegistry>();
        var buildCache = new Mock<IBuildCacheService>();
        using var intelliSense = CreateRealIntelliSense();

        var ctrl = new EditorsTabItemControl(vm, MakeTheme().Object, new Mock<IWindowService>().Object, registry.Object, prefs.Object,
            intelliSense, buildCache.Object, timings: NoPauses.Timings);
        var host = new Window { Content = ctrl };
        host.Show();

        var editor = ctrl.State.Editor;
        var margin = editor.TextArea.LeftMargins.OfType<CodeFixMargin>().Single();

        // Regex isn't in the implicit-usings preamble — CS0103 fixable by adding
        // `using System.Text.RegularExpressions;` (real CSharpAddImportCodeFixProvider fix).
        const string code = "var m = Regex.Match(\"a\", \"a\");";
        // editor.Text's setter already fires TextChanged -> NotifyTextChanged(editor.Text)
        // (SetupEditor's own wiring) — no need to also call NotifyTextChanged here, and doing so
        // would start a SECOND, independent debounced analysis pass that could replace
        // RoslynCodeFixEngine's cache (wholesale, by design) right as this test is mid-poll, handing
        // back a CodeFixEntry whose Key belongs to whichever pass finished first.
        editor.Text = code;

        // Multiple real fixes apply here (e.g. CSharpFullyQualifyCodeFixProvider's "Fully qualify
        // 'Regex'" ALSO mentions Regex in its title) — matching specifically on "using" picks out
        // CSharpAddImportCodeFixProvider's own fix, not just any Regex-related one.
        CodeFixEntry? FindUsingFix() =>
            margin.Fixes.FirstOrDefault(e => e.Title.Contains("using", StringComparison.OrdinalIgnoreCase));
        await TestWait.WhileAsync(() => FindUsingFix() is null);
        var entry = FindUsingFix();
        Assert.NotNull(entry);

        var item = margin.BuildMenuItem(entry);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        // A new untitled tab still gets a real MdRunDocument (every Invoklet script is a .inv document
        // — see NewMdRunFile), so this "add using" fix's namespace goes to FrontMatter.Namespaces
        // (docs/ImplementationPlanRPad.md — usings are project-level metadata), NOT into the visible
        // editor text (which is why the raw text is never expected to change here at all).
        // FrontMatter.Namespaces is already a real (if empty) list from tab creation onward — poll
        // for the NAMESPACE showing up, not merely for the list itself becoming non-null.
        await TestWait.WhileAsync(() =>
            ctrl.State.MdRunDocument?.FrontMatter.Namespaces?.Contains("System.Text.RegularExpressions") != true);
        var namespaces = ctrl.State.MdRunDocument?.FrontMatter.Namespaces;

        Assert.NotNull(namespaces);
        Assert.Contains("System.Text.RegularExpressions", namespaces);
        Assert.Equal(code, editor.Text); // visible script body itself is untouched
        await TestWait.WhileAsync(() => margin.Fixes.Any(f => f.DiagnosticId == "CS0103"));
    }
}
