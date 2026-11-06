using Invoklet.Languages.DotnetCommon.Compiler;
using Xunit;

namespace Invoklet.IntegrationTests;

// A new contract makes the older ones useless: they are deleted from the folder, on the real file system.
public sealed class ConnectionContractAssemblyCacheTests
{
    [Fact]
    public void RemoveOthers_AnOlderContractBesideTheKeptOne_DeletesTheOlderAndKeepsTheKept()
    {
        // Arrange
        using var fixture = new ConnectionContractAssemblyCacheFixture();
        var older = fixture.AddContract("aaaa");
        var kept = fixture.AddContract("bbbb");

        // Act
        ConnectionContractAssemblyCache.RemoveOthers(fixture.Folder, kept);

        // Assert
        Assert.False(File.Exists(older));
        Assert.False(File.Exists(Path.ChangeExtension(older, ".xml")));
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(Path.ChangeExtension(kept, ".xml")));
    }

    [Fact]
    public void RemoveOthers_NoContractToKeep_DeletesEveryContract()
    {
        // Arrange
        using var fixture = new ConnectionContractAssemblyCacheFixture();
        var contract = fixture.AddContract("aaaa");

        // Act
        ConnectionContractAssemblyCache.RemoveOthers(fixture.Folder, null);

        // Assert
        Assert.False(File.Exists(contract));
    }

    [Fact]
    public void RemoveOthers_AContractOnItsWayIn_LeavesItsFiles()
    {
        // Arrange
        using var fixture = new ConnectionContractAssemblyCacheFixture();
        var incoming = fixture.AddContractOnItsWayIn("cccc");

        // Act
        ConnectionContractAssemblyCache.RemoveOthers(fixture.Folder, null);

        // Assert
        Assert.True(File.Exists(incoming));
        Assert.True(File.Exists(incoming + ".xml"));
    }

    [Fact]
    public void RemoveOthers_TheScriptsOwnFiles_LeavesThem()
    {
        // Arrange
        using var fixture = new ConnectionContractAssemblyCacheFixture();
        var library = fixture.AddOtherFile("Script.dll");

        // Act
        ConnectionContractAssemblyCache.RemoveOthers(fixture.Folder, null);

        // Assert
        Assert.True(File.Exists(library));
    }
}
