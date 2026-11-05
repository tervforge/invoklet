using Xunit;

namespace Invoklet.Runtime.Tests;

[Trait("Platform", "Independent")]
public class StartupHookTests
{
    // What a program the debugged script starts inherits: the user's own hooks, without Invoklet's, however its path is written.
    [Theory]
    [InlineData("/bin/Invoklet.Runtime.dll", null)]
    [InlineData("/mine.dll|/bin/Invoklet.Runtime.dll", "/mine.dll")]
    [InlineData("/a.dll|/bin/Invoklet.Runtime.dll|/b.dll", "/a.dll|/b.dll")]
    [InlineData("/var/x/Invoklet.Runtime.dll", null)]
    [InlineData(null, null)]
    public void Without_Should_TakeOutOnlyItsOwnPath(string? hooks, string? expected)
    {
        var separator = Path.PathSeparator;

        var left = StartupHook.Without(hooks?.Replace('|', separator), "Invoklet.Runtime.dll");

        Assert.Equal(expected?.Replace('|', separator), left);
    }
}
