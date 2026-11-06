using Invoklet.Build.Core.ProcessRunners;
using Xunit;

namespace Invoklet.NonUI.Tests.Build.Core.ProcessRunners;

// A debugged script loads Invoklet.Runtime's startup hook, after any hook the user's environment already names.
public sealed class ChildProcessStartupHookTests
{
    [Fact]
    public void WithStartupHook_NoHooksNamed_NamesOnlyInvokletsOwn()
    {
        // Act
        var hooks = ChildProcess.WithStartupHook(null, "/bin/Invoklet.Runtime.dll");

        // Assert
        Assert.Equal("/bin/Invoklet.Runtime.dll", hooks);
    }

    [Fact]
    public void WithStartupHook_AHookAlreadyNamed_PutsInvokletsOwnAfterIt()
    {
        // Act
        var hooks = ChildProcess.WithStartupHook("/mine.dll", "/bin/Invoklet.Runtime.dll");

        // Assert
        Assert.Equal("/mine.dll" + Path.PathSeparator + "/bin/Invoklet.Runtime.dll", hooks);
    }
}
