using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

// A failed file operation is told in three parts: what failed on which file, why in plain words, and the folder on a line of its own.
public sealed class FileErrorTextTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Invoklet Queries");
    private static readonly string File = Path.Combine(Folder, "Live output.inv");

    [Fact]
    public void Describe_AccessDenied_SaysSoAndNamesTheFolderOnItsOwnLine()
    {
        // Arrange
        var denied = new UnauthorizedAccessException($"Access to the path '{File}' is denied.");

        // Act
        var text = FileErrorText.Describe("Could not save", File, denied);

        // Assert
        Assert.Equal($"Could not save 'Live output.inv'.\nAccess was denied.\n\n{Folder}", text);
    }

    // Any other failure keeps .NET's reason, with the file's name where it named the whole path.
    [Fact]
    public void Describe_AnotherFailure_KeepsTheReasonWithoutTheWholePath()
    {
        // Arrange
        var busy = new IOException($"The process cannot access the file '{File}' because it is being used by another process.");

        // Act
        var text = FileErrorText.Describe("Could not save", File, busy);

        // Assert
        Assert.Contains("\nThe process cannot access the file 'Live output.inv' because it is being used by another process.\n", text);
    }

    [Fact]
    public void Reason_AMissingFolder_SaysTheFolderIsGone()
    {
        // Act
        var reason = FileErrorText.Reason(new DirectoryNotFoundException("Could not find a part of the path."), File);

        // Assert
        Assert.Equal("Its folder no longer exists.", reason);
    }
}
