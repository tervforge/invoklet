using Invoklet.Languages.DotnetCommon.Compiler;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Compiler;

// A restore that downloads says what it is doing, from NuGet's own log lines, so a first run doesn't look stuck.
[Trait("Platform", "Independent")]
public sealed class NuGetRestorerProgressTests
{
    [Theory]
    [InlineData("  GET https://api.nuget.org/v3-flatcontainer/humanizer.core/2.8.26/humanizer.core.2.8.26.nupkg", "downloading humanizer.core 2.8.26")]
    [InlineData("Installed Humanizer.Core 2.8.26 from https://api.nuget.org/v3/index.json to /p with content hash x.", "installed Humanizer.Core 2.8.26")]
    [InlineData("  GET https://api.nuget.org/v3-flatcontainer/humanizer.core/index.json", "checking humanizer.core")]
    [InlineData("  OK https://api.nuget.org/v3-flatcontainer/humanizer.core/index.json 66ms", null)]
    [InlineData("  GET https://api.nuget.org/v3/index.json", null)]
    [InlineData("Writing assets file to disk. Path: /p/obj/project.assets.json", null)]
    public void DescribeProgress_ALogLine_SaysWhatTheRestoreIsDoing(string line, string? expected)
    {
        // Act
        var doing = NuGetRestorer.DescribeProgress(line);

        // Assert
        Assert.Equal(expected, doing);
    }
}
