using Invoklet.Build.Core.Models;
using Invoklet.Build.Core.ProcessRunners;
using Invoklet.Build.Core.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Build.Core.ProcessRunners;

// The Secret Store's key goes to the process of a script that may ask for secrets and to no other: not through Invoklet's own environment, which
// every program started later would inherit.
public class ChildProcessSecretKeyTests
{
    private static ExecutionOptions CreateOptions(string? secretKey = null) => new()
    {
        WorkingDirectory = Path.GetTempPath(),
        Runtime = new RuntimeOptions { RuntimePath = "dotnet" },
        Debugger = new DebuggerOptions { DebuggerPath = "netcoredbg" },
        SecretKey = secretKey,
    };

    [Fact]
    public void ForExecution_OptionsWithAKey_PassTheKeyInTheEnvironment()
    {
        var process = ChildProcess.ForExecution(CreateOptions("a2V5"), "out.dll", "123");

        Assert.Equal("a2V5", process.StartInfoForTests.Environment[SecretKeys.EnvVar]);
    }

    [Fact]
    public void ForExecution_OptionsWithoutAKey_LeaveTheKeyOut()
    {
        var process = ChildProcess.ForExecution(CreateOptions(), "out.dll", "123");

        Assert.False(process.StartInfoForTests.Environment.ContainsKey(SecretKeys.EnvVar));
    }

    [Fact]
    public void ForDebugger_OptionsWithAKey_PassTheKeyToTheDebuggerAndSoToTheProgram()
    {
        var process = ChildProcess.ForDebugger(CreateOptions("a2V5"));

        Assert.Equal("a2V5", process.StartInfoForTests.Environment[SecretKeys.EnvVar]);
    }

    [Fact]
    public void ApplyTo_NoKey_RemovesWhatWasInherited()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("x");
        startInfo.Environment[SecretKeys.EnvVar] = "inherited";

        SecretKeys.ApplyTo(startInfo, null);

        Assert.False(startInfo.Environment.ContainsKey(SecretKeys.EnvVar));
    }

    [Fact]
    public void ExportForScripts_KeepsTheKeyOutOfTheProcessEnvironment()
    {
        var before = Environment.GetEnvironmentVariable(SecretKeys.EnvVar);

        SecretKeys.ExportForScripts(new byte[32]);

        Assert.Equal(before, Environment.GetEnvironmentVariable(SecretKeys.EnvVar));
        Assert.Equal(Convert.ToBase64String(new byte[32]), SecretKeys.ScriptKey);
    }
}
