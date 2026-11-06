using Invoklet.Languages.DotnetCommon.Sdk;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk.Fixtures;

// The systems a search runs on, with made-up folders: nothing is looked at on the disk.
internal sealed class DotnetExecutableResolverFixture
{
    public string Home { get; } = Path.Combine("home", "me");

    public DotnetExecutableResolver.Search MacOS(string? dotnetRoot = null, params string[] registered) =>
        new(DotnetExecutableResolver.DotnetSystem.MacOS, Home, dotnetRoot, registered);

    public DotnetExecutableResolver.Search Linux() => new(DotnetExecutableResolver.DotnetSystem.Linux, Home, null, []);

    public DotnetExecutableResolver.Search Windows() =>
        new(DotnetExecutableResolver.DotnetSystem.Windows, Home, null, [], ProgramFiles: "pf", ProgramFilesX86: "pf86", LocalAppData: "local");

    public string UsersOwn(string host = "dotnet") => Path.Combine(Home, ".dotnet", host);
}
