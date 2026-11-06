using Invoklet.Services.Preferences;
using Invoklet.Services.Setup;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// The wizard over a fake installer, preferences and prerequisite check; the question before an install is answered by Answer.
internal sealed class SetupWizardViewModelFixture
{
    public Mock<ISetupInstallerService> Installer { get; } = new();

    public Mock<IPreferencesService> Preferences { get; } = new();

    public Mock<IPrerequisiteService> Prerequisites { get; } = new();

    public DebuggerSettings Debugger { get; } = new();

    public SetupInstallPlan SdkPlan { get; } = new(".NET SDK", "10.0.403", "/home/user/.dotnet", "Microsoft (dot.net)", false);

    public SetupInstallPlan DebuggerPlan { get; } = new("netcoredbg", "3.2.0-1092", "/home/user/netcoredbg", "GitHub (Samsung/netcoredbg)", true);

    // What the user answers when asked whether to install, and what they were asked about.
    public bool Answer { get; set; } = true;

    public List<SetupInstallPlan> Asked { get; } = [];

    public SetupWizardViewModelFixture()
    {
        Preferences.Setup(p => p.Debugger).Returns(Debugger);
        Prerequisites.Setup(p => p.Check()).Returns(new PrerequisiteStatus(false, false));
        Installer.Setup(i => i.PlanDotnetSdkAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SdkPlan);
        Installer.Setup(i => i.PlanNetcoredbgAsync(It.IsAny<CancellationToken>())).ReturnsAsync(DebuggerPlan);
    }

    public void PrerequisitesAre(bool sdk, bool debugger) =>
        Prerequisites.Setup(p => p.Check()).Returns(new PrerequisiteStatus(sdk, debugger));

    public void SdkInstallReturns(string path) =>
        Installer.Setup(i => i.InstallDotnetSdkAsync(SdkPlan, It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>())).ReturnsAsync(path);

    public void SdkInstallThrows(string message) =>
        Installer.Setup(i => i.InstallDotnetSdkAsync(SdkPlan, It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

    public void DebuggerInstallReturns(string path) =>
        Installer.Setup(i => i.InstallNetcoredbgAsync(DebuggerPlan, It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>())).ReturnsAsync(path);

    public void DebuggerInstallThrows(string message) =>
        Installer.Setup(i => i.InstallNetcoredbgAsync(DebuggerPlan, It.IsAny<IProgress<SetupProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

    public SetupWizardViewModel CreateSut()
    {
        var sut = new SetupWizardViewModel(Installer.Object, Preferences.Object, Prerequisites.Object);
        sut.ConfirmInstall.RegisterHandler(context =>
        {
            Asked.Add(context.Input);
            context.SetOutput(Answer);
        });
        return sut;
    }
}
