using Invoklet.Services.Setup;
using Xunit;

namespace Invoklet.IntegrationTests;

// The first-run installer with the network and the install script faked, extracting and placing files for real: it shows what it will
// install, then installs exactly that.
public sealed class SetupInstallerServiceTests
{
    [Fact]
    public async Task PlanDotnetSdkAsync_TheReleaseListAnswers_PlansItsLatestSdk()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        fixture.NetworkAnswersText("""{ "channel-version": "10.0", "latest-sdk": "10.0.403" }""");
        var sut = fixture.CreateSut();

        // Act
        var plan = await sut.PlanDotnetSdkAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("10.0.403", plan.Version);
    }

    [Fact]
    public async Task PlanDotnetSdkAsync_TheReleaseListCantBeReached_PlansTheLatestWithoutAVersion()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        fixture.NetworkFails();
        var sut = fixture.CreateSut();

        // Act
        var plan = await sut.PlanDotnetSdkAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(plan.Version);
    }

    // What comes from the network goes into the script's arguments only if it looks like a version.
    [Fact]
    public async Task PlanDotnetSdkAsync_TheAnswerIsNotAVersion_PlansTheLatestWithoutAVersion()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        fixture.NetworkAnswersText("""{ "latest-sdk": "10.0.403; rm -rf ~" }""");
        var sut = fixture.CreateSut();

        // Act
        var plan = await sut.PlanDotnetSdkAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(plan.Version);
    }

    [Fact]
    public async Task InstallDotnetSdkAsync_APlanWithAVersion_RunsTheScriptForThatVersionInThePlansFolder()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        var plan = fixture.SdkPlan("10.0.403");
        fixture.NetworkAnswersText("# the install script");
        fixture.ScriptEndsWith(0, plan);
        var sut = fixture.CreateSut();

        // Act
        var dotnet = await sut.InstallDotnetSdkAsync(plan, new Progress<SetupProgress>(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(plan.Folder, fixture.ScriptArguments);
        Assert.Contains("10.0.403", fixture.ScriptArguments);
        Assert.StartsWith(plan.Folder, dotnet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallDotnetSdkAsync_TheScriptFails_Throws()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        var plan = fixture.SdkPlan(null);
        fixture.NetworkAnswersText("# the install script");
        fixture.ScriptEndsWith(1, plan);
        var sut = fixture.CreateSut();

        // Act
        var install = () => sut.InstallDotnetSdkAsync(plan, new Progress<SetupProgress>(), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(install);
    }

    [Fact]
    public async Task InstallNetcoredbgAsync_APlanWithATag_DownloadsThatRelease()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        fixture.NetworkAnswers(SetupInstallerServiceFixture.WriteNetcoredbgArchive);
        var sut = fixture.CreateSut();

        // Act
        await sut.InstallNetcoredbgAsync(fixture.DebuggerPlan("3.2.0-1092"), new Progress<SetupProgress>(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("/releases/download/3.2.0-1092/", Assert.Single(fixture.Downloaded).AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallNetcoredbgAsync_AReleaseArchive_ExtractsTheDebuggerIntoTheFolder()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        fixture.NetworkAnswers(SetupInstallerServiceFixture.WriteNetcoredbgArchive);
        var sut = fixture.CreateSut();

        // Act
        var debugger = await sut.InstallNetcoredbgAsync(fixture.DebuggerPlan(null), new Progress<SetupProgress>(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith(fixture.NetcoredbgFolder, debugger, StringComparison.Ordinal);
        Assert.Equal("fake netcoredbg binary", await File.ReadAllTextAsync(debugger, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstallNetcoredbgAsync_TheArchiveHasNoDebugger_KeepsTheInstalledOne()
    {
        // Arrange
        using var fixture = new SetupInstallerServiceFixture();
        var installed = fixture.InstallWorkingDebugger();
        fixture.NetworkAnswers(SetupInstallerServiceFixture.WriteArchiveWithoutADebugger);
        var sut = fixture.CreateSut();

        // Act
        var install = () => sut.InstallNetcoredbgAsync(fixture.DebuggerPlan(null), new Progress<SetupProgress>(),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(install);
        Assert.Equal("working", await File.ReadAllTextAsync(installed, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(fixture.NetcoredbgFolder + ".new"));
    }
}
