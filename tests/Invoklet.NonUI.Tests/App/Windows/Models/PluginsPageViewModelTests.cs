using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Windows.Models;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

public sealed class PluginsPageViewModelTests
{
    private const string ReinstallHint = "Remove the plugin and install its package again to approve the new contents.";

    [Fact]
    public void Refresh_ChangedContentsOfAPreferencesPackage_ShowsContentChangedWithTheReinstallHint()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(PluginTrust.Trusted, PluginPackageInstaller.InstalledSource);

        // Act
        sut.Refresh();

        // Assert
        var row = Assert.Single(sut.Rows, item => item.Status == "Content changed");
        Assert.True(row.CanRemove);
        Assert.Contains(ReinstallHint, row.Diagnostics);
    }

    [Theory]
    [InlineData(PluginTrust.Trusted, "manual")]
    [InlineData(PluginTrust.Bundled, null)]
    public void Refresh_ChangedContentsThatCannotBeRemovedHere_ShowsContentChangedWithoutTheReinstallHint(PluginTrust trust, string? source)
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(trust, source);

        // Act
        sut.Refresh();

        // Assert
        var row = Assert.Single(sut.Rows, item => item.Status == "Content changed");
        Assert.False(row.CanRemove);
        Assert.DoesNotContain(ReinstallHint, row.Diagnostics);
    }

    [Fact]
    public async Task RemoveAsync_DeclinedConfirmation_ChangesNothing()
    {
        // Arrange
        var fixture = new PluginsPageViewModelFixture();
        var sut = fixture.CreateSut(PluginTrust.Trusted, PluginPackageInstaller.InstalledSource);
        sut.Refresh();

        // Act
        await sut.RemoveAsync(sut.Rows.Single(row => row.CanRemove), () => Task.FromResult(false));

        // Assert
        fixture.CreateInstaller.Verify(create => create(), Times.Never);
        Assert.Empty(sut.Diagnostics);
    }
}
