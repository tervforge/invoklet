using System.Windows.Input;
using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Secrets;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// The scripts allowed to use the secrets always are listed in the Secret Store window, and Revoke takes one's access away at once,
// without the window's Save.
public sealed class SecretStoreWindowViewModelRevokeTests
{
    [Fact]
    public void AllowedScripts_TheSettingsAllowTwo_ListsBoth()
    {
        // Arrange
        var fixture = new SecretStoreWindowViewModelRevokeFixture();

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.Equal(["/a/report.inv", "/a/other.inv"], sut.AllowedScripts);
    }

    [Fact]
    public void RevokeCommand_AScript_TakesItFromTheListAndTheSavedSettings()
    {
        // Arrange
        var fixture = new SecretStoreWindowViewModelRevokeFixture();
        var sut = fixture.CreateSut();

        // Act
        ((ICommand)sut.RevokeCommand).Execute("/a/report.inv");

        // Assert
        Assert.Equal(["/a/other.inv"], sut.AllowedScripts);
        Assert.Equal(["/a/other.inv"], fixture.Settings.AllowedScripts);
        Assert.False(fixture.Settings.AllowedScriptHashes.ContainsKey("/a/report.inv"));
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    // Revoking is not an edit of the secrets: nothing waits for Save, and the store isn't written.
    [Fact]
    public void RevokeCommand_AScript_LeavesTheSecretsUnchanged()
    {
        // Arrange
        var fixture = new SecretStoreWindowViewModelRevokeFixture();
        var sut = fixture.CreateSut();

        // Act
        ((ICommand)sut.RevokeCommand).Execute("/a/report.inv");

        // Assert
        Assert.False(sut.HasChanges);
        fixture.Store.Verify(s => s.Save(It.IsAny<IReadOnlyList<Secret>>(), It.IsAny<string?>()), Times.Never);
    }
}
