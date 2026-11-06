using Invoklet.Contracts;
using Invoklet.NonUI.Tests.App.Services.Secrets.Fixtures;
using Invoklet.Services;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services.Secrets;

// The first time a script asks for a secret the user is asked; Always is kept in the settings, Once until Invoklet closes, a no not at all.
public sealed class SecretAccessGateTests
{
    [Fact]
    public async Task MayAccessAsync_AlwaysAllowed_KeepsTheScriptInTheSettings()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Equal([Path.GetFullPath(fixture.ScriptPath)], fixture.Settings.AllowedScripts);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    [Fact]
    public async Task MayAccessAsync_AScriptAlwaysAllowedBefore_AsksNoMore()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        await fixture.CreateSut().MayAccessAsync(fixture.Request(), CancellationToken.None);
        fixture.Answer = SecretAccessChoice.Deny;
        var sut = fixture.CreateSut();

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request("other", isSave: true), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Equal([("report.inv", "billing", false)], fixture.Asked);
    }

    // A file another program changed (a new download, a replaced copy) is not the file the user allowed.
    [Fact]
    public async Task MayAccessAsync_AnAllowedScriptChangedByAnotherProgram_AsksAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);
        fixture.FileContents = "BBBB";

        // Act
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.Equal(2, fixture.Asked.Count);
    }

    // The user's own edits, saved by Invoklet, keep the script allowed.
    [Fact]
    public async Task ScriptWritten_AnAllowedScriptSavedByInvoklet_KeepsItAllowed()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);
        byte[] saved = [1, 2, 3];
        fixture.FileContents = SecretAccessGateFixture.HashOf(saved);

        // Act
        sut.ScriptWritten(fixture.ScriptPath, saved);
        var allowed = await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Single(fixture.Asked);
    }

    [Fact]
    public async Task ScriptWritten_AWritableScriptSavedByInvoklet_KeepsWriteAccess()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request("db", isSave: true), CancellationToken.None);
        byte[] saved = [1, 2, 3];
        fixture.FileContents = SecretAccessGateFixture.HashOf(saved);

        // Act
        sut.ScriptWritten(fixture.ScriptPath, saved);
        var allowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Empty(fixture.ChangesAsked);
    }

    // An earlier version kept the path only: such a script is asked about once more, and its contents are kept then.
    [Fact]
    public async Task MayAccessAsync_AScriptAllowedWithoutItsContents_AsksAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        fixture.Settings.AllowedScripts.Add(fixture.ScriptPath);
        var sut = fixture.CreateSut();

        // Act
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.Single(fixture.Asked);
        Assert.Equal("AAAA", fixture.Settings.AllowedScriptHashes[fixture.ScriptPath]);
    }

    // A script allowed before may still not wipe what another script needs: replacing a kept secret is asked about that once.
    [Fact]
    public async Task MayAccessAsync_AnAllowedScriptApprovesAReplacement_RemembersWriteAccessForRemoval()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);
        var removalAllowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Remove), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.True(removalAllowed);
        Assert.Equal([("report.inv", "db", false)], fixture.ChangesAsked);
        Assert.Equal([Path.GetFullPath(fixture.ScriptPath)], fixture.Settings.WritableScripts);
    }

    [Fact]
    public async Task MayAccessAsync_AnAllowedScriptCreatesASecret_GrantsWriteAccessWithoutASecondPrompt()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Act
        var created = await sut.MayAccessAsync(fixture.Request("db", isSave: true), CancellationToken.None);
        var replaced = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);

        // Assert
        Assert.True(created);
        Assert.True(replaced);
        Assert.Empty(fixture.ChangesAsked);
        Assert.Equal([Path.GetFullPath(fixture.ScriptPath)], fixture.Settings.WritableScripts);
    }

    [Fact]
    public async Task MayAccessAsync_AWritableScriptChangedOutsideInvoklet_AsksForAccessAndWriteAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request("db", isSave: true), CancellationToken.None);
        fixture.FileContents = "BBBB";

        // Act
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);
        var replaced = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);

        // Assert
        Assert.True(replaced);
        Assert.Equal(2, fixture.Asked.Count);
        Assert.Single(fixture.ChangesAsked);
        Assert.Equal([Path.GetFullPath(fixture.ScriptPath)], fixture.Settings.WritableScripts);
    }

    [Fact]
    public async Task MayAccessAsync_SessionWritePermissionAndExternalChange_AsksForWriteAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Once };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request("db", isSave: true), CancellationToken.None);
        await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);
        fixture.FileContents = "BBBB";

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Single(fixture.ChangesAsked);
    }

    [Fact]
    public async Task MayAccessAsync_ARemovalTheUserRefuses_IsNotAllowed()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always, AllowChange = false };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Remove), CancellationToken.None);

        // Assert
        Assert.False(allowed);
        Assert.Equal([("report.inv", "db", true)], fixture.ChangesAsked);
        Assert.Empty(fixture.Settings.WritableScripts);
    }

    // The first question already says the script wants to change the secret: it isn't asked twice in a row.
    [Fact]
    public async Task MayAccessAsync_AScriptAskedAboutJustNow_IsNotAskedAboutTheChangeToo()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Once };
        var sut = fixture.CreateSut();

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request("db", isSave: true, change: SecretChange.Replace), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Empty(fixture.ChangesAsked);
    }

    // Always is kept only in the settings: taking it away there (the Secret Store window) makes the next request ask again at once.
    [Fact]
    public async Task MayAccessAsync_AlwaysTakenAwayInTheSettings_AsksAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);
        fixture.Settings.AllowedScripts.Clear();

        // Act
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.Equal(2, fixture.Asked.Count);
    }

    // The script was stopped while the question was open: whatever the dialog then says, the script gets no secret and nothing is kept.
    [Fact]
    public async Task MayAccessAsync_TheRunStoppedDuringTheQuestion_RefusesAndKeepsNothing()
    {
        // Arrange
        using var stopped = new CancellationTokenSource();
        var fixture = new SecretAccessGateFixture { StoppedWhileAsking = stopped };
        var sut = fixture.CreateSut();

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request(), stopped.Token);

        // Assert
        Assert.False(allowed);
        Assert.Empty(fixture.Settings.AllowedScripts);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public async Task MayAccessAsync_AllowedOnce_AsksNoMoreWhileInvokletRunsAndSavesNothing()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Once };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Single(fixture.Asked);
        Assert.Empty(fixture.Settings.AllowedScripts);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    // Once lasts as long as this Invoklet: a new one (a new gate) asks again.
    [Fact]
    public async Task MayAccessAsync_AllowedOnceByAnEarlierInvoklet_AsksAgain()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Once };
        await fixture.CreateSut().MayAccessAsync(fixture.Request(), CancellationToken.None);
        var sut = fixture.CreateSut();

        // Act
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.Equal(2, fixture.Asked.Count);
    }

    [Fact]
    public async Task MayAccessAsync_Denied_RefusesAndAsksAgainNextTime()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Deny };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request(), CancellationToken.None);

        // Assert
        Assert.False(allowed);
        Assert.Equal(2, fixture.Asked.Count);
    }

    // An unsaved script has no file to remember: Always Allow lasts until Invoklet closes, and nothing is saved.
    [Fact]
    public async Task MayAccessAsync_AnUnsavedScriptAlwaysAllowed_AllowsUntilInvokletClosesWithoutSaving()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { Answer = SecretAccessChoice.Always };
        var sut = fixture.CreateSut();
        await sut.MayAccessAsync(fixture.Request(unsaved: true), CancellationToken.None);

        // Act
        var allowed = await sut.MayAccessAsync(fixture.Request(unsaved: true), CancellationToken.None);

        // Assert
        Assert.True(allowed);
        Assert.Equal([("an unsaved script", "billing", false)], fixture.Asked);
        Assert.Empty(fixture.Settings.AllowedScripts);
    }

    // Two requests while the question is open (a script asking for two secrets, two runs at once) get one question.
    [Fact]
    public async Task MayAccessAsync_TwoRequestsWhileTheQuestionIsOpen_AsksOnceForBoth()
    {
        // Arrange
        var fixture = new SecretAccessGateFixture { OpenQuestion = new TaskCompletionSource<SecretAccessChoice>() };
        var sut = fixture.CreateSut();

        // Act
        var first = sut.MayAccessAsync(fixture.Request("a"), CancellationToken.None);
        var second = sut.MayAccessAsync(fixture.Request("b"), CancellationToken.None);
        fixture.OpenQuestion.SetResult(SecretAccessChoice.Once);
        var results = await Task.WhenAll(first, second);

        // Assert
        Assert.All(results, Assert.True);
        Assert.Single(fixture.Asked);
    }
}
