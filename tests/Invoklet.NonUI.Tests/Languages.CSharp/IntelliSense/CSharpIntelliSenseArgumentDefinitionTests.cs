using Invoklet.Format.MdRun;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// Go to Definition on a typed argument of the script opens where it is declared: the class generated from the front matter's args.
[Trait("Platform", "Independent")]
public sealed class CSharpIntelliSenseArgumentDefinitionTests
{
    [Fact]
    public async Task GetDefinitionAsync_ATypedArgument_OpensTheGeneratedArgumentsAtItsDeclaration()
    {
        // Arrange
        using var sut = new CSharpIntelliSenseFixture().CreateSut();
        sut.UpdateArgumentContract(new MdRunArgumentContract([new("Count", "int", ["3"], [])]));
        const string code = "var n = Arguments.Count;";

        // Act
        var definition = await sut.GetDefinitionAsync(code, code.IndexOf("Count", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        // Assert
        var generated = Assert.NotNull(definition?.GeneratedDocument);
        Assert.Contains("Count", generated.Text.Split('\n')[generated.Line - 1], StringComparison.Ordinal);
    }
}
