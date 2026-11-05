using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Build.Core.ProcessRunners;

// No process Invoklet starts gets the Secret Store's key: a script asks Invoklet for one value, and a key in the environment of whoever
// started Invoklet goes no further.
public class ChildProcessSecretKeyTests
{
    private static ExecutionOptions CreateOptions() => new()
    {
        WorkingDirectory = Path.GetTempPath(),
        Runtime = new RuntimeOptions { RuntimePath = "dotnet" },
        Debugger = new DebuggerOptions { DebuggerPath = "netcoredbg" },
    };

    [Fact]
    public void ForExecution_And_ForDebugger_Should_TakeAwayAnInheritedKey()
    {
        var previous = Environment.GetEnvironmentVariable(SecretKeys.EnvVar);
        Environment.SetEnvironmentVariable(SecretKeys.EnvVar, "aW5oZXJpdGVk");
        try
        {
            Assert.False(ChildProcess.ForExecution(CreateOptions(), "out.dll", "123").StartInfoForTests.Environment.ContainsKey(SecretKeys.EnvVar));
            Assert.False(ChildProcess.ForDebugger(CreateOptions()).StartInfoForTests.Environment.ContainsKey(SecretKeys.EnvVar));
        }
        finally { Environment.SetEnvironmentVariable(SecretKeys.EnvVar, previous); }
    }
}
