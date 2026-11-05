using Invoklet.Contracts;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Services.Secrets;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services.Secrets;

// The first time a script asks for a secret the user is asked; Always is kept in the settings, Once until Invoklet closes, a no not at all.
public sealed class SecretAccessGateTests
{
    private static readonly string Script = Path.Combine(Path.GetTempPath(), "report.inv");

    private readonly SecretsSettings _settings = new();
    private readonly Mock<IPreferencesService> _preferences = new();
    private readonly List<(string Script, string Secret, bool IsSave)> _asked = [];

    public SecretAccessGateTests() => _preferences.SetupGet(p => p.Secrets).Returns(_settings);

    private SecretAccessGate CreateSut(SecretAccessChoice answer) => new(_preferences.Object, (script, secret, isSave) =>
    {
        _asked.Add((script, secret, isSave));
        return Task.FromResult(answer);
    });

    [Fact]
    public async Task AlwaysAllow_Should_BeKeptForTheFile_AndAskedNoMore()
    {
        var sut = CreateSut(SecretAccessChoice.Always);

        Assert.True(await sut.MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));
        Assert.True(await sut.MayAccessAsync(new SecretAccess(Script, "other", true), CancellationToken.None));
        Assert.True(await CreateSut(SecretAccessChoice.Deny).MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));

        Assert.Equal([("report.inv", "billing", false)], _asked);
        Assert.Equal([Path.GetFullPath(Script)], _settings.AllowedScripts);
        _preferences.Verify(p => p.Save(), Times.Once);
    }

    [Fact]
    public async Task AllowOnce_Should_LastUntilInvokletCloses_AndNotBeSaved()
    {
        var sut = CreateSut(SecretAccessChoice.Once);

        Assert.True(await sut.MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));
        Assert.True(await sut.MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));
        Assert.True(await CreateSut(SecretAccessChoice.Once).MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));

        Assert.Equal(2, _asked.Count);
        Assert.Empty(_settings.AllowedScripts);
        _preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public async Task DontAllow_Should_Refuse_AndAskAgainNextTime()
    {
        var sut = CreateSut(SecretAccessChoice.Deny);

        Assert.False(await sut.MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));
        Assert.False(await sut.MayAccessAsync(new SecretAccess(Script, "billing", false), CancellationToken.None));

        Assert.Equal(2, _asked.Count);
    }

    // An unsaved script has no file to remember: Always Allow lasts until Invoklet closes, and nothing is saved.
    [Fact]
    public async Task AnUnsavedScript_Should_BeAllowedOnlyUntilInvokletCloses()
    {
        var sut = CreateSut(SecretAccessChoice.Always);

        Assert.True(await sut.MayAccessAsync(new SecretAccess(null, "billing", false), CancellationToken.None));
        Assert.True(await sut.MayAccessAsync(new SecretAccess(null, "billing", false), CancellationToken.None));

        Assert.Equal([("an unsaved script", "billing", false)], _asked);
        Assert.Empty(_settings.AllowedScripts);
    }

    // Two requests while the question is open (a script asking for two secrets, two runs at once) get one question.
    [Fact]
    public async Task RequestsWhileTheQuestionIsOpen_Should_BeAskedOnce()
    {
        var answer = new TaskCompletionSource<SecretAccessChoice>();
        var asked = 0;
        var sut = new SecretAccessGate(_preferences.Object, (_, _, _) =>
        {
            asked++;
            return answer.Task;
        });

        var first = sut.MayAccessAsync(new SecretAccess(Script, "a", false), CancellationToken.None);
        var second = sut.MayAccessAsync(new SecretAccess(Script, "b", false), CancellationToken.None);
        answer.SetResult(SecretAccessChoice.Once);

        var results = await Task.WhenAll(first, second);
        Assert.All(results, Assert.True);
        Assert.Equal(1, asked);
    }
}
