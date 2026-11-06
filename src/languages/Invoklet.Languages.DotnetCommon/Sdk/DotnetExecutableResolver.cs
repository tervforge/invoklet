using System.Runtime.InteropServices;

namespace Invoklet.Languages.DotnetCommon.Sdk;

/// <summary>
/// Finds the dotnet host when none is set: the one the terminal runs (PATH), then where .NET says it is installed, then the usual
/// install folders of the system. An app started from the Finder, the Dock or a desktop menu gets a short PATH, so the folders matter.
/// </summary>
public static class DotnetExecutableResolver
{
    /// <summary>The dotnet host to run: <paramref name="configuredPath"/> when one is set, otherwise the one found (see the class).</summary>
    public static string Resolve(string? configuredPath)
    {
        if (!IsAutomatic(configuredPath)) return configuredPath!.Trim();

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirectories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return ResolveFromCandidates("dotnet", pathDirectories, InstallLocations(CurrentSearch())) ?? "dotnet";
    }

    /// <summary>Whether <paramref name="configuredPath"/> leaves the choice to <see cref="Resolve"/>: empty, or the bare command.</summary>
    public static bool IsAutomatic(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath) || string.Equals(configuredPath.Trim(), "dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is one of the places <see cref="Resolve"/> looks in by itself, so storing it says nothing.</summary>
    public static bool IsInstallLocation(string path) => IsInstallLocation(path, CurrentSearch());

    internal static bool IsInstallLocation(string path, Search search) =>
        InstallLocations(search).Contains(path.Trim(), search.System == DotnetSystem.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    internal static string? ResolveFromCandidates(
        string command,
        IEnumerable<string> pathDirectories,
        IEnumerable<string> standardPaths)
    {
        foreach (var directory in pathDirectories)
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? command + ".exe" : command);
            if (File.Exists(candidate)) return candidate;
        }

        return standardPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>The system and the folders the search depends on.</summary>
    /// <param name="Registered">The install folders .NET registers (<c>/etc/dotnet/install_location</c>, the Windows registry), first.</param>
    internal sealed record Search(DotnetSystem System, string Home, string? DotnetRoot, IReadOnlyList<string> Registered,
        string ProgramFiles = "", string ProgramFilesX86 = "", string LocalAppData = "");

    internal enum DotnetSystem { Windows, MacOS, Linux }

    /// <summary>The dotnet hosts to try after PATH, in order.</summary>
    internal static IReadOnlyList<string> InstallLocations(Search search)
    {
        var host = search.System == DotnetSystem.Windows ? "dotnet.exe" : "dotnet";
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(search.DotnetRoot)) folders.Add(search.DotnetRoot.Trim());
        folders.AddRange(search.Registered);
        folders.AddRange(search.System switch
        {
            DotnetSystem.Windows =>
            [
                Path.Combine(search.ProgramFiles, "dotnet"),
                Path.Combine(search.ProgramFilesX86, "dotnet"),
                // Where dotnet-install.ps1 puts it.
                Path.Combine(search.LocalAppData, "Microsoft", "dotnet"),
            ],
            DotnetSystem.MacOS =>
            [
                // The installer from Microsoft, and Homebrew's cask, which runs it.
                "/usr/local/share/dotnet",
                // Homebrew's formula, on Apple silicon and on Intel.
                "/opt/homebrew/opt/dotnet/libexec",
                "/usr/local/opt/dotnet/libexec",
            ],
            _ =>
            [
                // The packages of the distribution (Ubuntu, Fedora), then Microsoft's, then a manual install, then the snap.
                "/usr/lib/dotnet",
                "/usr/share/dotnet",
                "/usr/local/share/dotnet",
                "/snap/dotnet-sdk/current",
            ],
        });
        // Where dotnet-install.sh, Invoklet's setup and some editors put it: last, as it is one user's own.
        folders.Add(Path.Combine(search.Home, ".dotnet"));
        return [.. folders.Where(f => f.Length > 0).Select(f => Path.Combine(f, host)).Distinct()];
    }

    private static Search CurrentSearch()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (OperatingSystem.IsWindows())
            return new Search(DotnetSystem.Windows, home, root, RegisteredOnWindows(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return new Search(OperatingSystem.IsMacOS() ? DotnetSystem.MacOS : DotnetSystem.Linux, home, root, RegisteredOnUnix());
    }

    private static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    // What the .NET installers write: /etc/dotnet/install_location_<arch>, or /etc/dotnet/install_location, a folder on the first line.
    private static IReadOnlyList<string> RegisteredOnUnix()
    {
        foreach (var file in new[] { "/etc/dotnet/install_location_" + Architecture, "/etc/dotnet/install_location" })
        {
            try
            {
                if (File.Exists(file) && File.ReadLines(file).FirstOrDefault()?.Trim() is { Length: > 0 } folder) return [folder];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return [];
    }

    // What the .NET installer writes on Windows, in the 32-bit view of the registry whatever the architecture.
    private static IReadOnlyList<string> RegisteredOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32);
            using var key = hive.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\" + Architecture);
            return key?.GetValue("InstallLocation") is string { Length: > 0 } folder ? [folder] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
