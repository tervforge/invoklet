using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-atomic").FullName;

    public void Dispose()
    {
        // A test may have made the folder or a file read-only.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var file in Directory.EnumerateFiles(_folder)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_folder, true);
    }

    private string PathOf(string name) => Path.Combine(_folder, name);

    [Fact]
    public void WriteAllBytes_ANewFileAndAnExistingOne_HoldTheBytesAndLeaveNothingElseInTheFolder()
    {
        // Arrange
        var path = PathOf("a.inv");

        // Act
        AtomicFile.WriteAllBytes(path, [1, 2, 3]);
        AtomicFile.WriteAllBytes(path, [4, 5]);

        // Assert
        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task WriteAllBytesAsync_AnExistingFile_IsReplacedByTheBytes()
    {
        // Arrange
        var path = PathOf("b.txt");
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // Act
        await AtomicFile.WriteAllBytesAsync(path, [7], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([7], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void WriteAllBytes_TheFolderCannotBeWrittenTo_LeavesTheOldFileAsItWas()
    {
        // The folder refuses a new file: the old bytes must not have been touched, and nothing is left behind.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = PathOf("c.inv");
        File.WriteAllBytes(path, [1, 1, 1]);
        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.WriteAllBytes(path, [2]));

        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal([1, 1, 1], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void WriteAllBytes_AReadOnlyFile_IsRefusedAsWritingIntoItWas()
    {
        // Arrange
        var path = PathOf("d.inv");
        File.WriteAllBytes(path, [1]);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Act and assert
        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.WriteAllBytes(path, [2]));
        Assert.Equal([1], File.ReadAllBytes(path));
        Assert.Single(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void WriteAllBytes_OnUnix_KeepsThePermissionsOfTheFile()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = PathOf("e.sh");
        File.WriteAllBytes(path, [1]);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);

        AtomicFile.WriteAllBytes(path, [2]);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
    }

    [Fact]
    public void WriteAllBytes_ASymbolicLink_StaysALinkAndItsTargetIsReplaced()
    {
        // Arrange
        var target = PathOf("real.inv");
        var link = PathOf("link.inv");
        File.WriteAllBytes(target, [1]);
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // Windows without the privilege

        // Act
        AtomicFile.WriteAllBytes(link, [2]);

        // Assert
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal([2], File.ReadAllBytes(target));
        Assert.Equal([2], File.ReadAllBytes(link));
    }
}
