namespace Invoklet.Services.Setup;

public interface ISetupInstallerService
{
    /// <summary>Which .NET SDK an install would put where, asking Microsoft's release list for the version.</summary>
    Task<SetupInstallPlan> PlanDotnetSdkAsync(CancellationToken ct);

    /// <summary>Installs <paramref name="plan"/> with the official dotnet-install script. Returns the dotnet executable's path.</summary>
    Task<string> InstallDotnetSdkAsync(SetupInstallPlan plan, IProgress<SetupProgress> progress, CancellationToken ct);

    /// <summary>Which netcoredbg release an install would put where, asking GitHub for the latest one.</summary>
    Task<SetupInstallPlan> PlanNetcoredbgAsync(CancellationToken ct);

    /// <summary>Downloads and extracts <paramref name="plan"/> from Samsung's GitHub releases. Returns the netcoredbg executable's path.</summary>
    Task<string> InstallNetcoredbgAsync(SetupInstallPlan plan, IProgress<SetupProgress> progress, CancellationToken ct);
}
