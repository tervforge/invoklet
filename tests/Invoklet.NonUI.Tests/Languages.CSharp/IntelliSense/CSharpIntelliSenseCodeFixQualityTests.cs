using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// Live user report (2026-07-27) — three quick fixes to the lightbulb/code-fix pipeline (real Roslyn
// CodeFixProviders/CodeRefactoringProviders, not mocked — same rationale as every other
// CSharpIntelliSense*Tests file: the thing under test is whether Invoklet's synthetic preamble/kind-
// wrapped document survives real provider execution intact).
[Trait("Platform", "Independent")]
public class CSharpIntelliSenseCodeFixQualityTests
{
    private static readonly TimeSpan BackgroundAnalysisTimeout = TimeSpan.FromSeconds(30);

    // "offers to generate a constructor for '__InvokletProgram'" — MdRunKind.Program wraps the user's code in
    // a synthetic class (MdRunKindTransform.ProgramWrapperClassName) that's a REAL class in the live
    // analysis document, never shown to the user. Confirmed via a live probe (before the fix, with
    // RoslynCodeFixEngine's filter temporarily disabled) that caret position 0 — on the "int x;" field
    // declaration below — makes Roslyn's own generate-constructor refactoring fire against it,
    // producing the exact literal titles "Generate constructor '__InvokletProgram(int x)'" and
    // "Generate constructor '__InvokletProgram()'".
    [Fact]
    public async Task GetRefactoringsAsync_Should_NotOfferToGenerateAConstructor_OnTheSyntheticProgramWrapperClass()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        service.UpdateKind(MdRunKind.Program);
        const string code = "int x;\n\nvoid Main()\n{\n    Console.WriteLine(x);\n}\n";

        var refactorings = await service.GetRefactoringsAsync(code, 0, TestContext.Current.CancellationToken);

        Assert.NotNull(refactorings);
        Assert.DoesNotContain(refactorings, r => r.Title.Contains("__InvokletProgram", StringComparison.Ordinal));
    }

    // "If on one line ... JsonSerializer.Serialize(result, new JsonSerializerOptions {}...) then the
    // import gets suggested twice" — JsonSerializer and JsonSerializerOptions are two SEPARATE
    // undefined-type diagnostics on the same line, both fixed by the same `using System.Text.Json;` —
    // each independently discovered and offered that fix, so the lightbulb showed it twice.
    [Fact]
    public async Task NotifyTextChanged_Should_OfferTheSameUsingFix_OnlyOnce_WhenTwoUndefinedTypesOnOneLineNeedTheSameNamespace()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        var tcs = new TaskCompletionSource<IReadOnlyList<ICodeFixInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CodeFixesChanged += (_, f) =>
        {
            if (f.Any(x => x.Title.Contains("System.Text.Json", StringComparison.Ordinal))) tcs.TrySetResult(f);
        };

        service.NotifyTextChanged(
            "var output = JsonSerializer.Serialize(new { a = 1 }, new JsonSerializerOptions());\n");

        var fixes = await tcs.Task.WaitAsync(BackgroundAnalysisTimeout, TestContext.Current.CancellationToken);
        // Exact title match, not a substring — Roslyn also independently offers "Fully Qualify" fixes
        // for each occurrence ("System.Text.Json.JsonSerializer", "System.Text.Json.JsonSerializerOptions")
        // that legitimately mention the namespace too but are NOT duplicates of each other or of the
        // "using" fix — only the true duplicate ("using System.Text.Json;", offered independently by
        // both undefined-type diagnostics) is what this test is about.
        var usingFixes = fixes.Where(f => f.Title == "using System.Text.Json;").ToList();

        var onlyLine = Assert.Single(usingFixes);
        Assert.Equal(1, onlyLine.Line);
    }

    [Theory]
    [InlineData("void Main() {}", false)]
    [InlineData("void Main() {} void Other() {}", true)]
    public async Task NotifyTextChanged_Should_CountDistinctDiagnosticSpans_BeforeDeduplicatingActions(string code, bool multiple)
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        service.UpdateAccessibilityModifiersStyle("always");
        service.UpdateKind(MdRunKind.Program);
        var completion = new TaskCompletionSource<IReadOnlyList<ICodeFixInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CodeFixesChanged += (_, fixes) => completion.TrySetResult(fixes);

        service.NotifyTextChanged(code);

        var fixes = await completion.Task.WaitAsync(BackgroundAnalysisTimeout, TestContext.Current.CancellationToken);
        var fix = Assert.Single(fixes, f => f.DiagnosticId == "IDE0040" && f.Title == "Add accessibility modifiers");
        Assert.True(fix.SupportsFixAll);
        Assert.Equal(multiple, fix.HasMultipleDiagnosticOccurrences);
    }

    [Theory]
    [InlineData(FixAllScope.ContainingMember, 2)]
    [InlineData(FixAllScope.ContainingType, 3)]
    [InlineData(FixAllScope.Document, 4)]
    public async Task MaterializeFixAllAsync_Should_AddBraces_OnlyInsideTheSelectedScope(FixAllScope scope, int expectedBlocks)
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = """
            class A
            {
                void First(bool flag)
                {
                    if (flag) Console.WriteLine(1);
                    if (flag) Console.WriteLine(2);
                }
                void Second(bool flag) { if (flag) Console.WriteLine(3); }
            }
            class B { void Third(bool flag) { if (flag) Console.WriteLine(4); } }
            """;
        var completion = new TaskCompletionSource<IReadOnlyList<ICodeFixInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CodeFixesChanged += (_, fixes) => completion.TrySetResult(fixes);
        var ct = TestContext.Current.CancellationToken;
        service.NotifyTextChanged(code);
        var fixes = await completion.Task.WaitAsync(BackgroundAnalysisTimeout, ct);
        var selected = Assert.Single(fixes, f => f.Line == 5 && f.DiagnosticId == "IDE0011" && f.Title == "Add braces");

        var applied = await service.MaterializeFixAllAsync(code, selected.Key, scope, ct);

        Assert.NotNull(applied);
        var root = CSharpSyntaxTree.ParseText(applied.NewCode, cancellationToken: ct).GetRoot(ct);
        Assert.False(root.ContainsDiagnostics);
        var statements = root.DescendantNodes().OfType<IfStatementSyntax>().ToList();
        Assert.Equal(4, statements.Count);
        Assert.Equal(Enumerable.Range(0, 4).Select(i => i < expectedBlocks), statements.Select(s => s.Statement is BlockSyntax));
    }

    [Fact]
    public async Task MaterializeFixAllAsync_Should_AddAccessibilityModifiers_InProgramMode()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        service.UpdateAccessibilityModifiersStyle("always");
        service.UpdateKind(MdRunKind.Program);
        const string code = "void Main() {}\nvoid Other() {}";
        var completion = new TaskCompletionSource<IReadOnlyList<ICodeFixInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CodeFixesChanged += (_, fixes) => completion.TrySetResult(fixes);
        var ct = TestContext.Current.CancellationToken;
        service.NotifyTextChanged(code);
        var fixes = await completion.Task.WaitAsync(BackgroundAnalysisTimeout, ct);
        var selected = fixes.First(f => f.DiagnosticId == "IDE0040" && f.Title == "Add accessibility modifiers");

        var applied = await service.MaterializeFixAllAsync(code, selected.Key, FixAllScope.Document, ct);

        Assert.NotNull(applied);
        Assert.Contains("private void Main()", applied.NewCode);
        Assert.Contains("private void Other()", applied.NewCode);
        Assert.DoesNotContain("__InvokletProgram", applied.NewCode);
    }

    [Fact]
    public async Task UpdateAccessibilityModifiersStyle_Should_ReanalyzeTheOpenScript()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        var passes = System.Threading.Channels.Channel.CreateUnbounded<IReadOnlyList<ICodeFixInfo>>();
        service.CodeFixesChanged += (_, fixes) => passes.Writer.TryWrite(fixes);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(BackgroundAnalysisTimeout);
        service.UpdateAccessibilityModifiersStyle("always");
        service.NotifyTextChanged("class First { }\ninternal class Second { }");

        var always = await passes.Reader.ReadAsync(timeout.Token);
        service.UpdateAccessibilityModifiersStyle("never");
        var never = await passes.Reader.ReadAsync(timeout.Token);
        service.UpdateAccessibilityModifiersStyle("omit_if_default");
        var omit = await passes.Reader.ReadAsync(timeout.Token);

        Assert.Equal(1, Assert.Single(always, fix => fix.DiagnosticId == "IDE0040").Line);
        Assert.DoesNotContain(never, fix => fix.DiagnosticId == "IDE0040");
        Assert.Equal(2, Assert.Single(omit, fix => fix.DiagnosticId == "IDE0040").Line);
    }

    // "like in ReSharper ... a 'using all' that automatically resolves all imports" — ResolveAllMissingUsingsAsync
    // resolves every unambiguous unresolved-type diagnostic in one pass instead of the lightbulb's fix
    // one at a time. Three undefined types, two different (but each individually unambiguous)
    // namespaces, on three different lines.
    [Fact]
    public async Task ResolveAllMissingUsingsAsync_Should_ResolveEveryUnambiguousMissingType_InOnePass()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code =
            "var list = new List<int>();\n" +
            "var output = JsonSerializer.Serialize(list, new JsonSerializerOptions());\n" +
            "var sb = new StringBuilder();\n";

        var resolved = await service.ResolveAllMissingUsingsAsync(code, TestContext.Current.CancellationToken);

        Assert.Equal(code, resolved.NewCode); // never touches the visible text, only usings
        Assert.Contains("System.Text.Json", resolved.UsingsToAdd);
        Assert.Contains("System.Text", resolved.UsingsToAdd);
        // List<int> is covered by ImplicitUsings.Default (System.Collections.Generic) already —
        // nothing to resolve for that line, and nothing should be added redundantly for it.
        Assert.Equal(2, resolved.UsingsToAdd.Count);
    }

    [Fact]
    public async Task ResolveAllMissingUsingsAsync_Should_ReturnNoUsings_WhenNothingIsUnresolved()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "Console.WriteLine(\"hi\");\n";

        var resolved = await service.ResolveAllMissingUsingsAsync(code, TestContext.Current.CancellationToken);

        Assert.Empty(resolved.UsingsToAdd);
    }

    // A genuinely ambiguous unresolved type (more than one equally-valid candidate namespace) must be
    // left alone rather than guessing — silently picking the wrong one would be worse than not fixing
    // it at all. "Timer" resolves to both System.Threading.Timer and System.Timers.Timer with no other
    // hint to disambiguate by.
    [Fact]
    public async Task ResolveAllMissingUsingsAsync_Should_SkipAnAmbiguousType_RatherThanGuessing()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "Timer t = null;\n";

        var resolved = await service.ResolveAllMissingUsingsAsync(code, TestContext.Current.CancellationToken);

        Assert.Empty(resolved.UsingsToAdd);
    }
}
