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
}
