using Invoklet.Contracts.IntelliSense;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Analysis;

// A secret written to the output stays in the results and the run's saved output: the editor warns, and offers to take the output away.
// Through the real IntelliSense service, with Invoklet.Runtime referenced, as the editor runs it.
[Trait("Platform", "Independent")]
public sealed class SecretOutputAnalyzerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static CSharpIntelliSenseService CreateService()
    {
        var references = new CSharpReferenceService();
        return new CSharpIntelliSenseService(
            references.GetDefault().Append(references.GetExtensionReference()), NullLogger<CSharpIntelliSenseService>.Instance)
        {
            BackgroundAnalysisDebounce = TimeSpan.Zero,
        };
    }

    [Theory]
    [InlineData("Util.GetSecret(\"billing\").Dump();\n", ".Dump()")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.WriteLine($\"token {t}\");\n", "Console.WriteLine")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nvar u = t;\n(\"Bearer \" + u).Dump(\"auth\");\n", ".Dump()")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.Write(\"{0}\", t ?? \"none\");\n", "Console.Write")]
    public async Task ASecretWrittenToTheOutput_Should_BeWarnedAbout(string code, string sink)
    {
        using var service = CreateService();

        var warnings = await SecretWarningsAsync(service, code);

        var warning = Assert.Single(warnings);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'billing'", warning.Message);
        Assert.Contains(sink, warning.Message);
    }

    // What is computed from a secret is not the secret; output that never holds it says nothing about it.
    [Theory]
    [InlineData("var t = Util.GetSecret(\"billing\");\nt.Length.Dump();\n")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.WriteLine(\"connected\");\n")]
    [InlineData("var t = \"public\";\nt.Dump();\n")]
    public async Task OutputWithoutTheSecret_Should_NotBeWarnedAbout(string code)
    {
        using var service = CreateService();

        Assert.Empty(await SecretWarningsAsync(service, code));
    }

    // Only the Dump goes; the line goes only when what is left could not stand as a statement.
    [Theory]
    [InlineData("var t = Util.GetSecret(\"billing\").Dump();\nConsole.WriteLine(1);\n", "Remove .Dump() of the secret 'billing'",
        "var t = Util.GetSecret(\"billing\");\nConsole.WriteLine(1);\n")]
    [InlineData("Util.GetSecret(\"billing\").Dump(\"token\");\nConsole.WriteLine(1);\n", "Remove .Dump() of the secret 'billing'",
        "Util.GetSecret(\"billing\");\nConsole.WriteLine(1);\n")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nt.Dump();\nConsole.WriteLine(1);\n", "Remove .Dump() of the secret 'billing'",
        "var t = Util.GetSecret(\"billing\");\nConsole.WriteLine(1);\n")]
    [InlineData("var t = Util.GetSecret(\"billing\");\n// shows the token\nConsole.WriteLine(t);\nConsole.WriteLine(1);\n", "Remove the output of the secret 'billing'",
        "var t = Util.GetSecret(\"billing\");\n// shows the token\nConsole.WriteLine(1);\n")]
    public async Task TheFix_Should_TakeTheOutputAway_AndKeepTheRest(string code, string title, string expected)
    {
        using var service = CreateService();
        var listed = new TaskCompletionSource<IReadOnlyList<ICodeFixInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CodeFixesChanged += (analyzed, fixes) =>
        {
            if (analyzed == code && fixes.Any(f => f.Title == title)) listed.TrySetResult(fixes);
        };

        service.NotifyTextChanged(code);
        var fix = (await listed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken)).First(f => f.Title == title);
        var applied = await service.MaterializeFixAsync(code, fix.Key, TestContext.Current.CancellationToken);

        Assert.NotNull(applied);
        Assert.Equal(expected, applied.NewCode);
    }

    private static async Task<IReadOnlyList<IDiagnosticInfo>> SecretWarningsAsync(CSharpIntelliSenseService service, string code)
    {
        var analyzed = new TaskCompletionSource<IReadOnlyList<IDiagnosticInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DiagnosticsChanged += (text, diagnostics) =>
        {
            if (text == code) analyzed.TrySetResult(diagnostics);
        };
        service.NotifyTextChanged(code);
        var diagnostics = await analyzed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        return [.. diagnostics.Where(d => d.Message.Contains("is written to the output", StringComparison.Ordinal))];
    }
}
