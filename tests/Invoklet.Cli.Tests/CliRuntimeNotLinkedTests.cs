using Xunit;

namespace Invoklet.Cli.Tests;

// The runner puts Invoklet.Runtime beside itself for the scripts to compile against, and never links it: the runtime is the script's library.
public sealed class CliRuntimeNotLinkedTests
{
    [Fact]
    public void TheRunner_Should_NotLinkTheRuntime_AndHaveItBesideIt()
    {
        var runner = typeof(CliRunner).Assembly;

        Assert.DoesNotContain(runner.GetReferencedAssemblies(), name => name.Name == "Invoklet.Runtime");
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(runner.Location)!, "Invoklet.Runtime.dll")));
    }
}
