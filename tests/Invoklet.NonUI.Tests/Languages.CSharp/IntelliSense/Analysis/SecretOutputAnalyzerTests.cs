using Invoklet.Contracts.IntelliSense;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Analysis.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Analysis;

// A secret written to the output stays in the results and the run's saved output: the editor warns, and offers to take the output away.
// Through the real IntelliSense service, as the editor runs it.
[Trait("Platform", "Independent")]
public sealed class SecretOutputAnalyzerTests
{
    [Theory]
    [InlineData("Util.GetSecret(\"billing\").Dump();\n", ".Dump()")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.WriteLine($\"token {t}\");\n", "Console.WriteLine")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nvar u = t;\n(\"Bearer \" + u).Dump(\"auth\");\n", ".Dump()")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.Write(\"{0}\", t ?? \"none\");\n", "Console.Write")]
    public async Task NotifyTextChanged_ASecretWrittenToTheOutput_WarnsNamingTheSecretAndTheOutput(string code, string sink)
    {
        // Arrange
        using var sut = new SecretOutputAnalyzerFixture().CreateSut();
        var warnings = SecretOutputAnalyzerFixture.SecretWarnings(sut, code);

        // Act
        sut.NotifyTextChanged(code);
        var reported = await warnings.WaitAsync(SecretOutputAnalyzerFixture.Timeout, TestContext.Current.CancellationToken);

        // Assert
        var warning = Assert.Single(reported);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'billing'", warning.Message);
        Assert.Contains(sink, warning.Message);
    }

    // What is computed from a secret is not the secret; output that never holds it says nothing about it.
    [Theory]
    [InlineData("var t = Util.GetSecret(\"billing\");\nt.Length.Dump();\n")]
    [InlineData("var t = Util.GetSecret(\"billing\");\nConsole.WriteLine(\"connected\");\n")]
    [InlineData("var t = \"public\";\nt.Dump();\n")]
    public async Task NotifyTextChanged_OutputWithoutTheSecret_WarnsOfNothing(string code)
    {
        // Arrange
        using var sut = new SecretOutputAnalyzerFixture().CreateSut();
        var warnings = SecretOutputAnalyzerFixture.SecretWarnings(sut, code);

        // Act
        sut.NotifyTextChanged(code);
        var reported = await warnings.WaitAsync(SecretOutputAnalyzerFixture.Timeout, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(reported);
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
    public async Task MaterializeFixAsync_TheFixOfAWarning_TakesTheOutputAwayAndKeepsTheRest(string code, string title, string expected)
    {
        // Arrange
        using var sut = new SecretOutputAnalyzerFixture().CreateSut();
        var offered = SecretOutputAnalyzerFixture.Fix(sut, code, title);
        sut.NotifyTextChanged(code);
        var fix = await offered.WaitAsync(SecretOutputAnalyzerFixture.Timeout, TestContext.Current.CancellationToken);

        // Act
        var applied = await sut.MaterializeFixAsync(code, fix.Key, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(applied);
        Assert.Equal(expected, applied.NewCode);
    }
}
