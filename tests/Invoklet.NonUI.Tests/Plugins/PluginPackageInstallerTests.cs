using Invoklet.NonUI.Tests.Plugins.Fixtures;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

public sealed class PluginPackageInstallerTests : IDisposable
{
    private readonly PluginPackageInstallerFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Remove_AnInstalledPackageDependsOnIt_IsRefusedAndNamesTheDependant()
    {
        // Arrange
        _fixture.Install(PluginPackageInstallerFixture.Parent, "Ocean");
        _fixture.Install(PluginPackageInstallerFixture.Child, "Reef", dependsOn: PluginPackageInstallerFixture.Parent);
        var sut = _fixture.CreateSut();

        // Act
        var error = Assert.Throws<PluginRegistrationException>(() => sut.Remove(PluginPackageInstallerFixture.Parent));

        // Assert
        Assert.Equal("Remove the plugins that depend on 'example.theme.ocean' first: Reef.", error.Message);
        Assert.Equal(2, _fixture.Store.Read().Entries.Count);
        Assert.True(Directory.Exists(_fixture.InstalledDirectory(PluginPackageInstallerFixture.Parent)));
    }

    [Fact]
    public void Remove_NoPackageDependsOnIt_RemovesTheEntryAndTheFolder()
    {
        // Arrange
        _fixture.Install(PluginPackageInstallerFixture.Parent, "Ocean");
        _fixture.Install(PluginPackageInstallerFixture.Child, "Reef", dependsOn: PluginPackageInstallerFixture.Parent);
        var sut = _fixture.CreateSut();

        // Act
        sut.Remove(PluginPackageInstallerFixture.Child);

        // Assert
        Assert.Equal(PluginPackageInstallerFixture.Parent, Assert.Single(_fixture.Store.Read().Entries).Id);
        Assert.False(Directory.Exists(_fixture.InstalledDirectory(PluginPackageInstallerFixture.Child)));
    }
}
