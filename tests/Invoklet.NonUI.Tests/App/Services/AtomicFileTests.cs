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

    // umask is the process's, and it is what leaves a file readable by others: made 022 here, as it is on most systems.
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "umask")]
    private static extern uint Umask(uint mask);

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task WriteAllBytes_WhileTheTemporaryFileIsBeingWritten_NobodyButTheOwnerCanReadIt(bool async)
    {
        if (OperatingSystem.IsWindows()) return;
        // Arrange
        var path = PathOf("private.inv");
        File.WriteAllBytes(path, [1]);
        File.SetUnixFileMode(path, OwnerOnly);
        UnixFileMode? whileWriting = null;
        var old = Umask(0x12); // 022
        try
        {
            // Act
            if (async) await AtomicFile.WriteAllBytesAsync(path, [2], temp => whileWriting = File.GetUnixFileMode(temp), TestContext.Current.CancellationToken);
            else AtomicFile.WriteAllBytes(path, [2], temp => whileWriting = File.GetUnixFileMode(temp));
        }
        finally
        {
            Umask(old);
        }

        // Assert: the temporary file was the owner's from the moment it existed, and the file ends with the permissions it had.
        Assert.Equal(OwnerOnly, whileWriting);
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void WriteAllBytes_ANewFile_GetsThePermissionsANewFileGetsHere()
    {
        if (OperatingSystem.IsWindows()) return;
        var old = Umask(0x12); // 022: a new file is readable by others
        try
        {
            AtomicFile.WriteAllBytes(PathOf("new.inv"), [1]);
            File.WriteAllBytes(PathOf("plain.txt"), [1]);
        }
        finally
        {
            Umask(old);
        }

        Assert.Equal(File.GetUnixFileMode(PathOf("plain.txt")), File.GetUnixFileMode(PathOf("new.inv")));
        Assert.Equal([PathOf("new.inv"), PathOf("plain.txt")], Directory.EnumerateFileSystemEntries(_folder).Order());
    }
}
