using System.Text.Json;
using Invoklet.Services.BuildCache;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public sealed class BuildCacheServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-buildcache").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string IndexPath => Path.Combine(_folder, "index", "build-cache.json");

    private string WorkDir => Path.Combine(_folder, "work");

    // The process that runs it is the one application; a second instance is a second service on the same file.
    private BuildCacheService NewInstance() => new(NullLogger<BuildCacheService>.Instance, IndexPath);

    private string Script(string name) => Path.Combine(_folder, "scripts", name);

    private Dictionary<string, JsonElement> Index() => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(IndexPath))!;

    [Fact]
    public void EnsureOutputDirectory_TwoInstancesThatReadTheIndexBeforeEitherWrote_KeepBothEntries()
    {
        // Arrange
        var first = NewInstance();
        var second = NewInstance();

        // Act
        var a = first.EnsureOutputDirectory(Script("a.inv"), WorkDir);
        var b = second.EnsureOutputDirectory(Script("b.inv"), WorkDir);

        // Assert
        Assert.Equal(2, Index().Count);
        var later = NewInstance();
        Assert.Equal(a, later.EnsureOutputDirectory(Script("a.inv"), WorkDir));
        Assert.Equal(b, later.EnsureOutputDirectory(Script("b.inv"), WorkDir));
    }

    [Fact]
    public void EnsureOutputDirectory_AScriptAnotherInstanceAlreadyHasAFolderFor_GetsThatFolder()
    {
        // Arrange
        var first = NewInstance();
        var second = NewInstance();
        var made = first.EnsureOutputDirectory(Script("a.inv"), WorkDir);

        // Act
        var found = second.EnsureOutputDirectory(Script("a.inv"), WorkDir);

        // Assert
        Assert.Equal(made, found);
        Assert.Single(Directory.GetDirectories(WorkDir));
    }

    [Fact]
    public async Task EnsureOutputDirectory_TwoInstancesWorkAtTheSameTime_LoseNoEntry()
    {
        // Arrange
        var instances = new[] { NewInstance(), NewInstance() };

        // Act
        await Task.WhenAll(instances.Select((instance, n) => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++) instance.EnsureOutputDirectory(Script($"s{n}-{i}.inv"), WorkDir);
        }, TestContext.Current.CancellationToken)));

        // Assert
        Assert.Equal(50, Index().Count);
        Assert.Equal(50, Directory.GetDirectories(WorkDir).Length);
    }

    [Fact]
    public void Touch_AfterAnotherInstanceAddedAnEntry_KeepsThatEntry()
    {
        // Arrange
        var first = NewInstance();
        var second = NewInstance();
        first.EnsureOutputDirectory(Script("a.inv"), WorkDir);
        second.EnsureOutputDirectory(Script("b.inv"), WorkDir);

        // Act
        first.Touch(Script("a.inv"));

        // Assert
        Assert.Equal(2, Index().Count);
    }

    [Fact]
    public void PruneStale_AnOldEntry_IsRemovedWithItsFolderAndAFreshOneStays()
    {
        // Arrange
        var sut = NewInstance();
        var old = sut.EnsureOutputDirectory(Script("old.inv"), WorkDir);
        var fresh = sut.EnsureOutputDirectory(Script("fresh.inv"), WorkDir);

        // Act
        sut.PruneStale(TimeSpan.FromDays(-1)); // everything is older than tomorrow
        var again = sut.EnsureOutputDirectory(Script("fresh.inv"), WorkDir);

        // Assert
        Assert.False(Directory.Exists(old));
        Assert.NotEqual(fresh, again);
    }

    [Fact]
    public void EnsureOutputDirectory_TwoFilesThatDifferOnlyInCase_AreTheSameFileOnlyWhereTheSystemSaysSo()
    {
        // Arrange
        var sut = NewInstance();

        // Act
        var lower = sut.EnsureOutputDirectory(Script("a.inv"), WorkDir);
        var upper = sut.EnsureOutputDirectory(Script("A.inv"), WorkDir);
        var reloaded = NewInstance().EnsureOutputDirectory(Script("A.inv"), WorkDir);

        // Assert
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) Assert.Equal(lower, upper);
        else
        {
            Assert.NotEqual(lower, upper);
            Assert.Equal(upper, reloaded);
        }
    }

    // Another running Invoklet that holds the lock of the index for long (a clean-up deleting folders): the window does not wait for it, the
    // shared file is not written without the lock, the script has its folder, and the index gets the entry when the lock is free again.
    private FileStream HoldTheLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        return new FileStream(IndexPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void EnsureOutputDirectory_TheLockIsHeldForLong_DoesNotWaitForItAndLeavesTheSharedIndexAlone()
    {
        // Arrange
        var sut = NewInstance();
        sut.EnsureOutputDirectory(Script("before.inv"), WorkDir);
        var indexBefore = File.ReadAllText(IndexPath);
        string folder;
        var waited = System.Diagnostics.Stopwatch.StartNew();

        // Act
        using (HoldTheLock())
        {
            folder = sut.EnsureOutputDirectory(Script("a.inv"), WorkDir);
            waited.Stop();

            // Assert: the script has its folder at once, the same one when asked again, and the file is as it was.
            Assert.True(Directory.Exists(folder));
            Assert.Equal(folder, sut.EnsureOutputDirectory(Script("a.inv"), WorkDir));
            Assert.Equal(indexBefore, File.ReadAllText(IndexPath));
        }
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(2), $"waited {waited.Elapsed}");
    }

    [Fact]
    public void Update_TheLockIsFreeAgain_TheEntryMadeMeanwhileIsEnteredAndNothingIsLost()
    {
        // Arrange
        var sut = NewInstance();
        var other = NewInstance();
        string folder;
        using (HoldTheLock()) folder = sut.EnsureOutputDirectory(Script("a.inv"), WorkDir);

        // Act: another instance enters its own script, then this one's next change enters the entry it made.
        var otherFolder = other.EnsureOutputDirectory(Script("b.inv"), WorkDir);
        sut.Touch(Script("a.inv"));

        // Assert
        Assert.Equal(2, Index().Count);
        var later = NewInstance();
        Assert.Equal(folder, later.EnsureOutputDirectory(Script("a.inv"), WorkDir));
        Assert.Equal(otherFolder, later.EnsureOutputDirectory(Script("b.inv"), WorkDir));
    }

    [Fact]
    public void PruneStale_TheLockIsHeldForLong_ChangesNothingAndDeletesNothing()
    {
        // Arrange
        var sut = new BuildCacheService(NullLogger<BuildCacheService>.Instance, IndexPath) { BackgroundLockWait = TimeSpan.FromMilliseconds(100) };
        var folder = sut.EnsureOutputDirectory(Script("a.inv"), WorkDir);
        var indexBefore = File.ReadAllText(IndexPath);

        // Act
        using (HoldTheLock()) sut.PruneStale(TimeSpan.FromDays(-1));

        // Assert
        Assert.True(Directory.Exists(folder));
        Assert.Equal(indexBefore, File.ReadAllText(IndexPath));
    }

    [Fact]
    public void PruneStale_ADayOldEntryAndAFolderBeingDeleted_DoesNotHoldThisInstanceWhileItDeletes()
    {
        // Arrange
        var sut = NewInstance();
        var folder = sut.EnsureOutputDirectory(Script("a.inv"), WorkDir);
        File.WriteAllText(Path.Combine(folder, "x.bin"), "x");

        // Act
        sut.PruneStale(TimeSpan.FromDays(-1));

        // Assert: the folder is gone, and the index no longer lists it.
        Assert.False(Directory.Exists(folder));
        Assert.Empty(Index());
    }

    // Two instances open the same script while the index is locked: each has a folder of its own for it. The one that enters its folder first keeps
    // the index; the other goes over to it when it reads the index again, and a script opened again later gets that folder.
    [Fact]
    public void Touch_AnotherInstanceEnteredAFolderForTheSameScriptMeanwhile_TheIndexHasThatOneAndBothFollowIt()
    {
        // Arrange
        var first = NewInstance();
        var second = NewInstance();
        string mine;
        using (HoldTheLock()) mine = first.EnsureOutputDirectory(Script("same.inv"), WorkDir);

        // Act
        var theirs = second.EnsureOutputDirectory(Script("same.inv"), WorkDir);
        first.Touch(Script("same.inv"));
        var followed = first.EnsureOutputDirectory(Script("same.inv"), WorkDir);

        // Assert
        Assert.NotEqual(mine, theirs);
        Assert.Equal(theirs, followed);
        Assert.Equal(theirs, NewInstance().EnsureOutputDirectory(Script("same.inv"), WorkDir));
        Assert.Single(Index());
    }
}
