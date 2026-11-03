using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// CSharpIntelliSenseService.
// GetRenameInfoAsync shares its "find the symbol under the caret and every occurrence" scan with
// GetDocumentHighlightsAsync (see CSharpIntelliSenseDocumentHighlightsTests.cs), only adding a
// filter for symbols the user's own script doesn't declare.
[Trait("Platform", "Independent")]
public class CSharpIntelliSenseRenameTests
{
    [Theory]
    [InlineData("var alpha = 1;\nvar beta = 2;\nConsole.WriteLine(alpha);", "alpha", "beta", true)]   // local in same scope
    [InlineData("int Twice(int nn, int mm) { return nn * 2; }\nTwice(1, 2);", "nn", "mm", true)] // parameter
    [InlineData("var alpha = 1;\nif (alpha > 0) { var inner = 2; }", "alpha", "inner", false)]   // inner scope, declared after: no clash at a's spans
    [InlineData("var alpha = 1;\nConsole.WriteLine(alpha);", "alpha", "fresh", false)]
    [InlineData("var alpha = 1;\nConsole.WriteLine(alpha);", "alpha", "alpha", false)]               // same name: no-op
    [InlineData("var alpha = 1;\nConsole.WriteLine(alpha);", "alpha", "class", true)]            // reserved word
    public async Task CheckRenameAsync_Should_ReportConflicts(string code, string oldName, string newName, bool conflict)
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        var position = code.IndexOf(oldName, StringComparison.Ordinal);

        var result = await service.CheckRenameAsync(code, position, newName, TestContext.Current.CancellationToken);

        Assert.Equal(conflict, result is not null);
    }

    [Fact]
    public async Task GetRenameInfoAsync_Should_ReturnCurrentNameAndEveryOccurrence_ForALocalVariable()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "var value = 1;\nvalue = value + 1;\nConsole.WriteLine(value);";
        var position = code.IndexOf("value + 1", StringComparison.Ordinal);

        var result = await service.GetRenameInfoAsync(code, position, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("value", result!.CurrentName);
        Assert.Equal(4, result.Spans.Count);
        Assert.All(result.Spans, h => Assert.Equal("value", code.Substring(h.Start, h.Length)));
    }

    // A method/class/local the script itself declares must be renameable — the point of the
    // feature — even though it's also just "a symbol with a source location", same as the local
    // variable case above; this exercises a different declaration shape (method, not a local).
    [Fact]
    public async Task GetRenameInfoAsync_Should_ReturnInfo_ForAUserDeclaredMethod()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "void Greet() { Console.WriteLine(\"hi\"); }\nGreet();";
        var position = code.IndexOf("Greet();", StringComparison.Ordinal);

        var result = await service.GetRenameInfoAsync(code, position, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Greet", result!.CurrentName);
        Assert.Equal(2, result.Spans.Count); // the declaration + the one call
    }

    // The whole point of filtering by IsInSource: renaming call sites of a framework method
    // without also "renaming" the framework method itself would just break the script — Console.
    // WriteLine has no source location in the user's own document to rename AT.
    [Fact]
    public async Task GetRenameInfoAsync_Should_ReturnNull_ForAFrameworkSymbol()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "Console.WriteLine(\"hi\");";
        var position = code.IndexOf("WriteLine", StringComparison.Ordinal);

        var result = await service.GetRenameInfoAsync(code, position, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRenameInfoAsync_Should_ReturnNull_WhenCaretIsNotOnAnIdentifier()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut();
        const string code = "var value = 1;";
        var position = code.IndexOf('=', StringComparison.Ordinal);

        var result = await service.GetRenameInfoAsync(code, position, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
