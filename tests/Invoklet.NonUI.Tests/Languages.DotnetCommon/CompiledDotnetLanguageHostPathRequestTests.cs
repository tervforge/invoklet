using Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures;
using Xunit;
using static Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures.CompiledDotnetLanguageHostSecretRequestFixture;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon;

// A script asks Invoklet for a named path (Util.GetPath) over its stdout and gets the answer on its stdin, with nobody asked: the path
// (1), none (0), why the store can't be read (2) or why the name isn't valid (4).
[Trait("Category", "Integration")]
public sealed class CompiledDotnetLanguageHostPathRequestTests
{
    [Fact]
    public async Task RunAsync_AReadRequestForASavedPath_AnswersThePathAndAsksNobody()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture();
        var sut = fixture.CreateSut(Request("""{"Path":" data ","Read":true}"""));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("1", answer, StringComparison.Ordinal);
        Assert.Equal("/work/data", Payload(answer));
        Assert.Equal(["data"], fixture.ReadPaths);
        Assert.Empty(fixture.Asked);
        Assert.False(fixture.Prompted);
    }

    [Fact]
    public async Task RunAsync_AReadRequestForAMissingPath_Answers0()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Paths = () => null };
        var sut = fixture.CreateSut(Request("""{"Path":"data","Read":true}"""));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0\n", answer);
    }

    [Fact]
    public async Task RunAsync_AReadRequestTheStoreCantAnswer_Answers2WithWhy()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Paths = () => throw new InvalidDataException("damaged") };
        var sut = fixture.CreateSut(Request("""{"Path":"data","Read":true}"""));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("2", answer, StringComparison.Ordinal);
        Assert.Contains("damaged", Payload(answer));
    }

    [Fact]
    public async Task RunAsync_AReadRequestWithAnInvalidName_Answers4WithWhyAndReadsNothing()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture();
        var sut = fixture.CreateSut(Request("""{"Path":"my data","Read":true}"""));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("4", answer, StringComparison.Ordinal);
        Assert.Contains("my data", Payload(answer));
        Assert.Empty(fixture.ReadPaths);
    }
}
