using Invoklet.Services.Setup;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services.Setup;

// What the setup says before it installs: which version, where, from where.
[Trait("Platform", "Independent")]
public sealed class SetupInstallPlanTests
{
    [Fact]
    public void Message_AKnownVersion_NamesItAndTheFolder()
    {
        // Arrange
        var sut = new SetupInstallPlan(".NET SDK", "10.0.403", "/home/me/.dotnet", "Microsoft (dot.net)", ReplacesExisting: false);

        // Act
        var message = sut.Message;

        // Assert
        Assert.Equal(".NET SDK 10.0.403 will be installed in /home/me/.dotnet, for your account only. It is downloaded from Microsoft (dot.net).", message);
    }

    [Fact]
    public void Message_AnUnknownVersionReplacingACopy_SaysTheLatestAndTheReplacement()
    {
        // Arrange
        var sut = new SetupInstallPlan("netcoredbg", null, "/home/me/netcoredbg", "GitHub (Samsung/netcoredbg)", ReplacesExisting: true);

        // Act
        var message = sut.Message;

        // Assert
        Assert.StartsWith("The latest release of netcoredbg will be installed in /home/me/netcoredbg", message, StringComparison.Ordinal);
        Assert.EndsWith("The copy already there is replaced.", message, StringComparison.Ordinal);
    }
}
