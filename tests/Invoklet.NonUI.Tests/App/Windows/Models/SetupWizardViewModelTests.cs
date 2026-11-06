using System.Reactive.Linq;
using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Services.Setup;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// The first-run setup: what it installs is asked about first (which version, where), and what it installed or was pointed at is saved.
[Trait("Platform", "Independent")]
public sealed class SetupWizardViewModelTests
{
    [Fact]
    public void Constructor_PrerequisitesMissing_ShowsThemMissing()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.False(sut.HasSuitableSdk);
        Assert.False(sut.HasValidDebugger);
    }

    [Fact]
    public async Task InstallSdkCommand_Always_AsksAboutThePlanBeforeInstalling()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.SdkInstallReturns("/home/user/.dotnet/dotnet");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallSdkCommand.Execute();

        // Assert
        Assert.Equal([fixture.SdkPlan], fixture.Asked);
    }

    [Fact]
    public async Task InstallSdkCommand_TheUserDeclines_InstallsAndSavesNothing()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture { Answer = false };
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallSdkCommand.Execute();

        // Assert
        Assert.False(sut.IsBusy);
        Assert.Equal("", sut.StatusText);
        fixture.Installer.Verify(i => i.InstallDotnetSdkAsync(It.IsAny<SetupInstallPlan>(),
            It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public async Task InstallSdkCommand_TheInstallSucceeds_ReportsItAndRefreshesTheStatus()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.SdkInstallReturns("/home/user/.dotnet/dotnet");
        var sut = fixture.CreateSut();
        fixture.PrerequisitesAre(sdk: true, debugger: false);

        // Act
        await sut.InstallSdkCommand.Execute();

        // Assert
        Assert.True(sut.HasSuitableSdk);
        Assert.False(sut.IsBusy);
        Assert.Contains("installed", sut.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // The installer only puts the SDK on this process's PATH: the path is saved for the next start.
    [Fact]
    public async Task InstallSdkCommand_TheInstallSucceeds_SavesTheInstalledDotnet()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.SdkInstallReturns("/home/user/.dotnet/dotnet");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallSdkCommand.Execute();

        // Assert
        Assert.Equal("/home/user/.dotnet/dotnet", fixture.Debugger.DotnetPath);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    [Fact]
    public async Task InstallSdkCommand_TheInstallFails_ShowsWhyAndKeepsTheDotnetPath()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.Debugger.DotnetPath = "/custom/dotnet";
        fixture.SdkInstallThrows("network unreachable");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallSdkCommand.Execute();

        // Assert
        Assert.False(sut.IsBusy);
        Assert.Contains("network unreachable", sut.StatusText);
        Assert.Equal("/custom/dotnet", fixture.Debugger.DotnetPath);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public async Task InstallDebuggerCommand_Always_AsksAboutThePlanBeforeInstalling()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.DebuggerInstallReturns("/home/user/netcoredbg/netcoredbg");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallDebuggerCommand.Execute();

        // Assert
        Assert.Equal([fixture.DebuggerPlan], fixture.Asked);
    }

    [Fact]
    public async Task InstallDebuggerCommand_TheUserDeclines_InstallsAndSavesNothing()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture { Answer = false };
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallDebuggerCommand.Execute();

        // Assert
        fixture.Installer.Verify(i => i.InstallNetcoredbgAsync(It.IsAny<SetupInstallPlan>(),
            It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public async Task InstallDebuggerCommand_TheInstallSucceeds_SavesTheInstalledDebugger()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.DebuggerInstallReturns("/home/user/netcoredbg/netcoredbg");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallDebuggerCommand.Execute();

        // Assert
        Assert.Equal("/home/user/netcoredbg/netcoredbg", fixture.Debugger.DebuggerPath);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
    }

    // Once nothing is left to do, the last install's message goes.
    [Fact]
    public async Task InstallDebuggerCommand_BothPrerequisitesMetAfterwards_ClearsTheStatus()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.PrerequisitesAre(sdk: true, debugger: false);
        fixture.DebuggerInstallReturns("/home/user/netcoredbg/netcoredbg");
        var sut = fixture.CreateSut();
        fixture.PrerequisitesAre(sdk: true, debugger: true);

        // Act
        await sut.InstallDebuggerCommand.Execute();

        // Assert
        Assert.Equal("", sut.StatusText);
    }

    // A failure is something left to fix, whatever the prerequisites read.
    [Fact]
    public async Task InstallDebuggerCommand_TheInstallFails_KeepsTheErrorEvenWithBothPrerequisitesMet()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        fixture.PrerequisitesAre(sdk: true, debugger: true);
        fixture.DebuggerInstallThrows("archive extracted but the exe wasn't found");
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallDebuggerCommand.Execute();

        // Assert
        Assert.Contains("archive extracted", sut.StatusText);
        fixture.Preferences.Verify(p => p.Save(), Times.Never);
    }

    [Fact]
    public void SetDotnetPathManually_APath_SavesItAndRefreshesTheStatus()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        var sut = fixture.CreateSut();
        fixture.PrerequisitesAre(sdk: true, debugger: false);

        // Act
        sut.SetDotnetPathManually("/custom/place/dotnet");

        // Assert
        Assert.Equal("/custom/place/dotnet", fixture.Debugger.DotnetPath);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
        Assert.True(sut.HasSuitableSdk);
    }

    [Fact]
    public void SetDebuggerPathManually_APath_SavesItAndRefreshesTheStatus()
    {
        // Arrange
        var fixture = new SetupWizardViewModelFixture();
        var sut = fixture.CreateSut();
        fixture.PrerequisitesAre(sdk: true, debugger: true);

        // Act
        sut.SetDebuggerPathManually("/custom/place/netcoredbg");

        // Assert
        Assert.Equal("/custom/place/netcoredbg", fixture.Debugger.DebuggerPath);
        fixture.Preferences.Verify(p => p.Save(), Times.Once);
        Assert.True(sut.HasValidDebugger);
    }
}
