using Xunit;

namespace Invoklet.Runtime.Tests;

// What a program the debugged script starts inherits: the user's own hooks, without Invoklet's, however its path is written.
public sealed class StartupHookTests
{
    [Theory]
    [InlineData("/bin/Invoklet.Runtime.dll", null)]
    [InlineData("/mine.dll|/bin/Invoklet.Runtime.dll", "/mine.dll")]
    [InlineData("/a.dll|/bin/Invoklet.Runtime.dll|/b.dll", "/a.dll|/b.dll")]
    [InlineData("/var/x/Invoklet.Runtime.dll", null)]
    [InlineData(null, null)]
    public void Without_HooksNamingInvokletsOwn_LeavesOnlyTheOthers(string? hooks, string? expected)
    {
        // Arrange
        var named = hooks?.Replace('|', Path.PathSeparator);

        // Act
        var left = StartupHook.Without(named, "Invoklet.Runtime.dll");

        // Assert
        Assert.Equal(expected?.Replace('|', Path.PathSeparator), left);
    }
}
