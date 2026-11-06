using Invoklet.Cli;
using Xunit;

namespace Invoklet.Cli.Tests;

// The folder a script run from the command line is built in: kept between runs, locked while one runs, on the real file system.
public sealed class CliBuildFolderTests
{
    [Fact]
    public void Open_TheSameScriptAfterAnEarlierRun_UsesTheSameFolder()
    {
        // Arrange
        using var fixture = new CliBuildFolderFixture();
        string first;
        using (var earlier = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath)) first = earlier.Path;

        // Act
        using var sut = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath);

        // Assert
        Assert.Equal(first, sut.Path);
        Assert.StartsWith(fixture.Root, sut.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_TheScriptIsRunningAlready_BuildsInATemporaryFolder()
    {
        // Arrange
        using var fixture = new CliBuildFolderFixture();
        using var running = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath);

        // Act
        using var sut = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath);

        // Assert
        Assert.NotEqual(running.Path, sut.Path);
        Assert.DoesNotContain(fixture.Root, sut.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_ATemporaryFolder_DeletesIt()
    {
        // Arrange
        var sut = CliBuildFolder.Open(null, "report.inv");

        // Act
        sut.Dispose();

        // Assert
        Assert.False(Directory.Exists(sut.Path));
    }

    [Fact]
    public void Dispose_AKeptFolder_KeepsIt()
    {
        // Arrange
        using var fixture = new CliBuildFolderFixture();
        var sut = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath);

        // Act
        sut.Dispose();

        // Assert
        Assert.True(Directory.Exists(sut.Path));
    }

    [Fact]
    public void PruneStale_AFolderUnusedForLongerThanTheAge_DeletesItAndItsLock()
    {
        // Arrange
        using var fixture = new CliBuildFolderFixture();
        var unused = fixture.AddUnusedFolder("unused");

        // Act
        CliBuildFolder.PruneStale(fixture.Root, fixture.MaxAge);

        // Assert
        Assert.False(Directory.Exists(unused));
        Assert.False(File.Exists(unused + ".lock"));
    }

    [Fact]
    public void PruneStale_AFolderARunHolds_KeepsIt()
    {
        // Arrange
        using var fixture = new CliBuildFolderFixture();
        using var running = CliBuildFolder.Open(fixture.Root, fixture.ScriptPath);
        Directory.SetLastWriteTimeUtc(running.Path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        CliBuildFolder.PruneStale(fixture.Root, fixture.MaxAge);

        // Assert
        Assert.True(Directory.Exists(running.Path));
    }
}
