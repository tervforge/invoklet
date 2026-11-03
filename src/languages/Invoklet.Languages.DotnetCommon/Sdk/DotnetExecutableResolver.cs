namespace Invoklet.Languages.DotnetCommon.Sdk;

/// <summary>Resolves the default dotnet host for GUI processes with a minimal PATH.</summary>
public static class DotnetExecutableResolver
{
    public static string Resolve(string? configuredPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? "dotnet" : configuredPath.Trim();
        if (!string.Equals(value, "dotnet", StringComparison.OrdinalIgnoreCase))
            return value;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirectories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return ResolveFromCandidates("dotnet", pathDirectories, CandidatePaths()) ?? value;
    }

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

    private static IEnumerable<string> CandidatePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[]
            {
                Path.Combine(programFiles, "dotnet", "dotnet.exe"),
                Path.Combine(programFilesX86, "dotnet", "dotnet.exe"),
                Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe"),
            };
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new List<string> { Path.Combine(home, ".dotnet", "dotnet") };
        if (OperatingSystem.IsMacOS())
        {
            paths.Insert(0, "/usr/local/share/dotnet/dotnet");
            paths.Insert(1, "/opt/homebrew/share/dotnet/dotnet");
        }
        else
        {
            paths.Insert(0, "/usr/share/dotnet/dotnet");
            paths.Insert(1, "/usr/local/share/dotnet/dotnet");
        }

        return paths;
    }
}
