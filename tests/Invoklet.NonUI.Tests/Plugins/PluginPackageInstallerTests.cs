using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

public sealed class PluginPackageInstallerTests
{
    [Theory]
    [Trait("Platform", "Independent")]
    [InlineData("theme.zip")]
    [InlineData("theme.invplugin")]
    [InlineData("theme")]
    public void PrepareArchive_NotANuGetPackage_IsRejectedBeforeItIsOpened(string path)
    {
        // Arrange: nothing at these paths exists; a rejected path is never opened.
        var sut = new PluginPackageInstaller("plugins", new PluginInventoryStore("plugins.json"), BundledPluginCatalog.Empty);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => sut.PrepareArchive(path));

        // Assert
        Assert.Equal("Choose a NuGet package (.nupkg).", error.Message);
    }
}
