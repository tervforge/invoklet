using Xunit;

namespace Invoklet.Cli.Tests;

// The runner puts Invoklet.Runtime beside itself for the scripts to compile against, and never links it: the runtime is the script's library.
public sealed class CliRuntimeNotLinkedTests
{
    [Fact]
    public void GetReferencedAssemblies_TheRunner_LeavesOutTheRuntime()
    {
        // Arrange
        var runner = typeof(CliRunner).Assembly;

        // Act
        var references = runner.GetReferencedAssemblies();

        // Assert
        Assert.DoesNotContain(references, name => name.Name == "Invoklet.Runtime");
    }

    [Fact]
    public void BuildOutput_TheRunner_HasTheRuntimeBesideIt()
    {
        // Arrange
        var folder = Path.GetDirectoryName(typeof(CliRunner).Assembly.Location)!;

        // Act
        var runtimeThere = File.Exists(Path.Combine(folder, "Invoklet.Runtime.dll"));

        // Assert
        Assert.True(runtimeThere);
    }
}
