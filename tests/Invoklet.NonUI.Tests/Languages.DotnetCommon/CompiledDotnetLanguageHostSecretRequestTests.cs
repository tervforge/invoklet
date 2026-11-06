using Invoklet.Contracts;
using Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon;

// A script asks Invoklet for a secret over its stdout and gets the answer on its stdin, with nobody prompted: Util.GetSecret is answered
// with the value (1), none (0) or why the store can't be read (2); Util.SetSecret with kept (1) or not (0); a script the user didn't let
// use the store with 3, and it reaches no secret.
[Trait("Category", "Integration")]
public sealed class CompiledDotnetLanguageHostSecretRequestTests
{
    private const string ReadRequest = """{"Secret":"db","Read":true}""";
    private const string SaveRequest = """{"Secret":"db","Value":"p@ss"}""";
    private const string RemoveRequest = """{"Secret":"db","Remove":true}""";

    [Fact]
    public async Task RunAsync_AReadRequestForAStoredSecret_AnswersItsValueAndPromptsNobody()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture();
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(ReadRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("1", answer, StringComparison.Ordinal);
        Assert.Equal("p@ss", CompiledDotnetLanguageHostSecretRequestFixture.Payload(answer));
        Assert.Equal(["db"], fixture.Read);
        Assert.False(fixture.Prompted);
    }

    [Fact]
    public async Task RunAsync_AReadRequestForAMissingSecret_Answers0()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Store = () => null };
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(ReadRequest));

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
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Store = () => throw new InvalidDataException("damaged") };
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(ReadRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("2", answer, StringComparison.Ordinal);
        Assert.Contains("damaged", CompiledDotnetLanguageHostSecretRequestFixture.Payload(answer));
    }

    [Fact]
    public async Task RunAsync_ASaveRequest_KeepsTheValueAndAnswers1()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture();
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(SaveRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1\n", answer);
        Assert.Equal([("db", (string?)"p@ss")], fixture.Saved);
        Assert.False(fixture.Prompted);
    }

    [Fact]
    public async Task RunAsync_ASaveRequestWithNowhereToKeepIt_Answers0()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { HasSaver = false };
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(SaveRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0\n", answer);
    }

    // Util.SetSecret(name, null) removes the secret: the saver is handed null.
    [Fact]
    public async Task RunAsync_ARemoveRequest_HandsTheSaverNull()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture();
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(RemoveRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1\n", answer);
        Assert.Equal([("db", (string?)null)], fixture.Saved);
    }

    [Fact]
    public async Task RunAsync_AReadRequestTheUserDidntAllow_Answers3AndReadsNothing()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Allowed = false };
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(ReadRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("3\n", answer);
        Assert.Empty(fixture.Read);
        Assert.Equal([new SecretAccess(null, "db", false)], fixture.Asked);
    }

    [Fact]
    public async Task RunAsync_ASaveRequestTheUserDidntAllow_Answers3AndSavesNothing()
    {
        // Arrange
        await using var fixture = new CompiledDotnetLanguageHostSecretRequestFixture { Allowed = false };
        var sut = fixture.CreateSut(CompiledDotnetLanguageHostSecretRequestFixture.Request(SaveRequest));

        // Act
        _ = sut.RunAsync("int x = 1;", (_, _) => { }, TestContext.Current.CancellationToken);
        var answer = await fixture.AnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("3\n", answer);
        Assert.Empty(fixture.Saved);
        Assert.Equal([new SecretAccess(null, "db", true)], fixture.Asked);
    }
}
