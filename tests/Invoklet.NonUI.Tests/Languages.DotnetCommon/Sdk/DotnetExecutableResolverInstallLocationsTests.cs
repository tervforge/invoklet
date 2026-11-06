using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk;

// Where dotnet is looked for after PATH, on each system: what .NET says first, the system's usual folders, one user's own last.
public sealed class DotnetExecutableResolverInstallLocationsTests
{
    [Fact]
    public void InstallLocations_MacOS_PutsMicrosoftsInstallFirstAndTheUsersOwnLast()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var locations = DotnetExecutableResolver.InstallLocations(fixture.MacOS());

        // Assert
        Assert.Equal(Path.Combine("/usr/local/share/dotnet", "dotnet"), locations[0]);
        Assert.Equal(fixture.UsersOwn(), locations[^1]);
    }

    [Fact]
    public void InstallLocations_DotnetRootAndARegisteredFolder_ComeBeforeTheUsualFolders()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var locations = DotnetExecutableResolver.InstallLocations(fixture.MacOS("/root", "/registered"));

        // Assert
        Assert.Equal([Path.Combine("/root", "dotnet"), Path.Combine("/registered", "dotnet")], locations.Take(2));
    }

    [Fact]
    public void InstallLocations_Linux_LooksInTheDistributionsPackagesFirst()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var locations = DotnetExecutableResolver.InstallLocations(fixture.Linux());

        // Assert
        Assert.Equal(Path.Combine("/usr/lib/dotnet", "dotnet"), locations[0]);
        Assert.Contains(Path.Combine("/usr/share/dotnet", "dotnet"), locations);
    }

    [Fact]
    public void InstallLocations_Windows_LooksForTheExeInProgramFilesFirst()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var locations = DotnetExecutableResolver.InstallLocations(fixture.Windows());

        // Assert
        Assert.Equal(Path.Combine("pf", "dotnet", "dotnet.exe"), locations[0]);
        Assert.Equal(fixture.UsersOwn("dotnet.exe"), locations[^1]);
    }

    [Fact]
    public void IsInstallLocation_ThePathOfAFolderTheSearchLooksIn_IsTrue()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var found = DotnetExecutableResolver.IsInstallLocation(fixture.UsersOwn(), fixture.MacOS());

        // Assert
        Assert.True(found);
    }

    [Fact]
    public void IsInstallLocation_AnotherPath_IsFalse()
    {
        // Arrange
        var fixture = new DotnetExecutableResolverFixture();

        // Act
        var found = DotnetExecutableResolver.IsInstallLocation(Path.Combine("opt", "custom", "dotnet"), fixture.MacOS());

        // Assert
        Assert.False(found);
    }
}
