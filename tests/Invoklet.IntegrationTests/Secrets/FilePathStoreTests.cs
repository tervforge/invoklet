using System.Runtime.Versioning;
using System.Text.Json;
using Invoklet.Secrets;
using Xunit;

namespace Invoklet.IntegrationTests;

// The Path Store's file on a real disk, in a folder of each test's own.
public sealed class FilePathStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-paths").FullName;

    private string StorePath => Path.Combine(_folder, "nested", "paths.json");

    private string At(string name) => Path.Combine(_folder, name);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void LoadSnapshot_NoStoreYet_IsEmptyWithoutAVersion()
    {
        // Act
        var snapshot = new FilePathStore(StorePath).LoadSnapshot();

        // Assert
        Assert.Empty(snapshot.Paths);
        Assert.Null(snapshot.Version);
    }

    [Fact]
    public void Save_ThenLoadInAnotherStore_GivesThePathsBackInOrder()
    {
        // Arrange
        NamedPath[] paths = [new("tools", At("tools")), new("Data", At("data"))];

        // Act
        new FilePathStore(StorePath).Save(paths, null);
        var loaded = new FilePathStore(StorePath).LoadSnapshot();

        // Assert
        Assert.Equal(paths, loaded.Paths);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Save_OnUnix_LeavesAFileOnlyItsOwnerCanReadAndWrite()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");

        // Act
        new FilePathStore(StorePath).Save([new("data", At("data"))], null);

        // Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public void Save_AfterAnotherWindowSaved_ThrowsAndKeepsTheirPaths()
    {
        // Arrange
        var mine = new FilePathStore(StorePath);
        var loaded = mine.LoadSnapshot();
        new FilePathStore(StorePath).Save([new("theirs", At("theirs"))], null);

        // Act
        var thrown = Record.Exception(() => mine.Save([new("mine", At("mine"))], loaded.Version));

        // Assert
        Assert.IsType<PathStoreConflictException>(thrown);
        Assert.Equal([new NamedPath("theirs", At("theirs"))], mine.LoadSnapshot().Paths);
    }

    [Fact]
    public void Put_AfterAnotherWindowSaved_KeepsBothPaths()
    {
        // Arrange
        var store = new FilePathStore(StorePath);
        store.Put("first", At("first"));
        new FilePathStore(StorePath).Put("second", At("second"));

        // Act
        store.Put("third", At("third"));

        // Assert
        Assert.Equal(["first", "second", "third"], store.LoadSnapshot().Paths.Select(p => p.Name));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Version\":1,\"Paths\":{\"data\":\"relative\"}}")]
    [InlineData("{\"Version\":1,\"Paths\":{\"my data\":\"/x\"}}")]
    [InlineData("{\"Version\":1,\"Paths\":{\"data\":null}}")]
    [InlineData("{\"Version\":0,\"Paths\":{}}")]
    public void LoadSnapshot_ADamagedFile_ThrowsAndLeavesIt(string content)
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, content);

        // Act
        var thrown = Record.Exception(() => new FilePathStore(StorePath).LoadSnapshot());

        // Assert
        Assert.IsType<InvalidDataException>(thrown);
        Assert.Equal(content, File.ReadAllText(StorePath));
    }

    [Fact]
    public void LoadSnapshot_AnExactDuplicateName_RejectsTheFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var first = JsonSerializer.Serialize(At("first"));
        var second = JsonSerializer.Serialize(At("second"));
        var content = $$"""{"Version":1,"Paths":{"data":{{first}},"data":{{second}}} }""";
        File.WriteAllText(StorePath, content);

        Assert.Throws<InvalidDataException>(() => new FilePathStore(StorePath).LoadSnapshot());
        Assert.Equal(content, File.ReadAllText(StorePath));
    }

    [Theory]
    [InlineData("*.csv")]
    [InlineData("data<old>")]
    [InlineData("a\"b")]
    [InlineData("one:two:three.txt")]
    public void LoadSnapshot_ALegacyInvalidWindowsPath_KeepsValidPathsReadableAndAllowsRepair(string fileName)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "These characters are legal in Unix file names.");
        var valid = At("data");
        var legacy = At(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new
        {
            Version = 1,
            Paths = new Dictionary<string, string> { ["data"] = valid, ["legacy"] = legacy },
        }));
        var store = new FilePathStore(StorePath);

        var snapshot = store.LoadSnapshot();
        Assert.Equal([new NamedPath("data", valid), new NamedPath("legacy", legacy)], snapshot.Paths);
        Assert.Equal(valid, store.Read("data"));
        Assert.Throws<InvalidDataException>(() => store.Read("legacy"));
        Assert.Throws<ArgumentException>(() => store.Save(snapshot.Paths, snapshot.Version));

        var repaired = At("input.csv");
        store.Save([snapshot.Paths[0], new("legacy", repaired)], snapshot.Version);
        Assert.Equal(repaired, store.Read("legacy"));
    }

    [Fact]
    public void LoadSnapshot_TwoNamesDifferingOnlyInCase_Throws()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var full = At("x").Replace("\\", "\\\\");
        File.WriteAllText(StorePath, $"{{\"Version\":1,\"Paths\":{{\"Data\":\"{full}\",\"data\":\"{full}\"}}}}");

        // Act
        var thrown = Record.Exception(() => new FilePathStore(StorePath).LoadSnapshot());

        // Assert
        Assert.Contains("two paths are named", Assert.IsType<InvalidDataException>(thrown).Message);
    }

    [Fact]
    public void LoadSnapshot_AFileFromANewerInvoklet_SaysSo()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "{\"Version\":2,\"Paths\":{}}");

        // Act
        var thrown = Record.Exception(() => new FilePathStore(StorePath).LoadSnapshot());

        // Assert
        Assert.Contains("newer Invoklet", Assert.IsType<InvalidDataException>(thrown).Message);
    }

    [Theory]
    [InlineData(" data")]
    [InlineData("my data")]
    public void Save_AnInvalidName_ThrowsAndWritesNothing(string name)
    {
        // Act
        var thrown = Record.Exception(() => new FilePathStore(StorePath).Save([new(name, At("data"))], null));

        // Assert
        Assert.IsType<ArgumentException>(thrown);
        Assert.False(File.Exists(StorePath));
    }
}
