using Invoklet.Contracts;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts;

[Trait("Platform", "Independent")]
public class LanguageHostRegistryTests
{
    private static ILanguageHostFactory Language(string id)
    {
        var factory = new Mock<ILanguageHostFactory>();
        factory.SetupGet(f => f.Language).Returns(id);
        factory.SetupGet(f => f.DisplayName).Returns(id);
        return factory.Object;
    }

    [Fact]
    public void Register_Should_RefuseASecondLanguageOfAnId_LetterCaseAside()
    {
        var registry = new LanguageHostRegistry();
        var first = Language("csharp");
        registry.Register(first);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(Language("CSharp")));

        Assert.Contains("CSharp", ex.Message);
        Assert.Equal(["csharp"], registry.SupportedLanguages);
        Assert.Equal("csharp", registry.GetDisplayName("CSHARP"));
    }

    [Fact]
    public void Register_Should_AcceptALanguageAgain_AfterItWasUnregistered()
    {
        var registry = new LanguageHostRegistry();
        registry.Register(Language("csharp"));
        registry.Register(Language("vb"));
        registry.Unregister("csharp");

        registry.Register(Language("csharp"));

        Assert.Equal(["vb", "csharp"], registry.SupportedLanguages);
    }
}
