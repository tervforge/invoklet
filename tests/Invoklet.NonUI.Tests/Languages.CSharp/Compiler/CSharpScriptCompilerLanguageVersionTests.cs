using Invoklet.Languages.CSharp.Compiler;
using Invoklet.NonUI.Tests.Languages.CSharp.Compiler.Fixtures;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.Compiler;

// The SDKs Invoklet offers end where the compiler's C# versions do: when the compiler is updated, both move together.
[Trait("Platform", "Independent")]
public sealed class CSharpScriptCompilerLanguageVersionTests
{
    [Fact]
    public void LanguageVersionFor_TheNewestTargetOffered_HasACSharpVersionOfItsOwn()
    {
        // Arrange
        var fixture = new CSharpScriptCompilerLanguageVersionFixture();

        // Act
        var version = CSharpScriptCompiler.LanguageVersionFor(fixture.NewestOffered);

        // Assert
        Assert.NotEqual(LanguageVersion.Default, version);
    }

    [Fact]
    public void LanguageVersionFor_TheFirstTargetNotOffered_HasNoCSharpVersionOfItsOwn()
    {
        // Arrange
        var fixture = new CSharpScriptCompilerLanguageVersionFixture();

        // Act
        var version = CSharpScriptCompiler.LanguageVersionFor(fixture.FirstNotOffered);

        // Assert
        Assert.Equal(LanguageVersion.Default, version);
    }
}
