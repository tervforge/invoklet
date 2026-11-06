using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// A script moved to another framework takes its connections' packages along, unless their version was chosen by hand.
[Trait("Platform", "Independent")]
public sealed class ConnectionPackagesTests
{
    [Fact]
    public void Retarget_APackageAtTheOldFrameworksRelease_MovesToTheNewOnes()
    {
        // Arrange
        var fixture = new ConnectionPackagesFixture();

        // Act
        var changes = ConnectionPackages.Retarget([fixture.Connection], fixture.Drivers(), ConnectionPackagesFixture.ScriptPackages("10.0.3"),
            "latest", "net8.0");

        // Assert
        Assert.Equal(new Dictionary<string, string> { [ConnectionPackagesFixture.Package] = "8.0.11" }, changes);
    }

    [Fact]
    public void Retarget_AVersionChosenByHand_LeavesIt()
    {
        // Arrange
        var fixture = new ConnectionPackagesFixture();

        // Act
        var changes = ConnectionPackages.Retarget([fixture.Connection], fixture.Drivers(), ConnectionPackagesFixture.ScriptPackages("10.0.1"),
            "latest", "net8.0");

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void Retarget_APackageTheScriptDoesNotHave_AddsNothing()
    {
        // Arrange
        var fixture = new ConnectionPackagesFixture();

        // Act
        var changes = ConnectionPackages.Retarget([fixture.Connection], fixture.Drivers(), ConnectionPackagesFixture.ScriptPackages(null),
            "latest", "net8.0");

        // Assert
        Assert.Empty(changes);
    }
}
