namespace Invoklet.Services.Setup;

/// <summary>What an install would put where, shown to the user before it starts.</summary>
/// <param name="Name">What is installed: ".NET SDK", "netcoredbg".</param>
/// <param name="Version">The release that will be installed; null when its source didn't answer, and the latest is installed.</param>
/// <param name="Folder">Where it goes.</param>
/// <param name="Source">Where it is downloaded from, in words.</param>
/// <param name="ReplacesExisting">Whether what is in <paramref name="Folder"/> now goes.</param>
public sealed record SetupInstallPlan(string Name, string? Version, string Folder, string Source, bool ReplacesExisting)
{
    public string Heading => $"Install {Name}?";

    // The folder on a line of its own: a long path wrapped inside a sentence broke in the middle of a name.
    public string Message =>
        (Version is null ? $"The latest release of {Name}" : $"{Name} {Version}")
        + $" will be installed for your account only, in\n{Folder}\n\nIt is downloaded from {Source}."
        + (ReplacesExisting ? " The copy already there is replaced." : "");
}
