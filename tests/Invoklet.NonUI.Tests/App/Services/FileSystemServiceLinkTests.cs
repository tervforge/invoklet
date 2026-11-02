using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

// A link to a folder is a folder in the tree, but one that leads back to a folder being walked (loop -> ..), or to a folder the workspace is
// inside of (root -> /), is listed without its content: the walk is finite and does not go over the disk.
public sealed class FileSystemServiceLinkTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-links").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Make(params string[] parts)
    {
        var path = Path.Combine([_folder, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // Windows without the privilege to make a link
        }
    }

    private static IReadOnlyList<FileSystemItem> Walk(string root) => FileSystemService.GetFilesTreeInternal(root, ct: TestContext.Current.CancellationToken);

    private static FileSystemItem Folder(IReadOnlyList<FileSystemItem> items, string name) => items.Single(i => i.Name == name);

    [Fact]
    public void ALinkToTheWorkspaceItself_IsListedWithoutContent()
    {
        var workspace = Make("ws");
        File.WriteAllText(Path.Combine(workspace, "a.inv"), "");
        if (!TryLink(Path.Combine(workspace, "loop"), workspace)) return;

        var tree = Walk(workspace);

        Assert.Empty(Folder(tree, "loop").Children!);
        Assert.Contains(tree, i => i.Name == "a.inv");
    }

    [Fact]
    public void ALinkToAFolderAboveTheOneItIsIn_IsListedWithoutContent()
    {
        var workspace = Make("ws");
        Make("ws", "sub");
        if (!TryLink(Path.Combine(workspace, "sub", "up"), workspace)) return;

        var tree = Walk(workspace);

        Assert.Empty(Folder(Folder(tree, "sub").Children!, "up").Children!);
    }

    [Fact]
    public void ALinkToAFolderThatTheWorkspaceIsInside_IsListedWithoutContent()
    {
        var workspace = Make("ws");
        Make("other");
        if (!TryLink(Path.Combine(workspace, "everything"), _folder)) return;

        var tree = Walk(workspace);

        Assert.Empty(Folder(tree, "everything").Children!);
    }

    [Fact]
    public void ALinkToAnotherFolder_IsWalkedAsAFolder_AlsoWhenTwoLinksLeadToTheSameOne()
    {
        var workspace = Make("ws");
        var shared = Make("shared");
        File.WriteAllText(Path.Combine(shared, "tool.inv"), "");
        if (!TryLink(Path.Combine(workspace, "first"), shared)) return;
        if (!TryLink(Path.Combine(workspace, "second"), shared)) return;

        var tree = Walk(workspace);

        Assert.Equal(["tool.inv"], Folder(tree, "first").Children!.Select(i => i.Name));
        Assert.Equal(["tool.inv"], Folder(tree, "second").Children!.Select(i => i.Name));
    }

    [Fact]
    public void AWorkspaceThatIsItselfALink_IsWalked()
    {
        var real = Make("real");
        File.WriteAllText(Path.Combine(real, "a.inv"), "");
        Make("real", "sub");
        var workspace = Path.Combine(_folder, "ws-link");
        if (!TryLink(workspace, real)) return;

        var tree = Walk(workspace);

        Assert.Equal(["sub", "a.inv"], tree.Select(i => i.Name));
    }

    [Fact]
    public void TwoLinksThatLeadToEachOther_AreWalkedOnceEach()
    {
        var workspace = Make("ws");
        var a = Make("a");
        var b = Make("b");
        if (!TryLink(Path.Combine(a, "to-b"), b)) return;
        if (!TryLink(Path.Combine(b, "to-a"), a)) return;
        if (!TryLink(Path.Combine(workspace, "start"), a)) return;

        var tree = Walk(workspace); // must end

        var toB = Folder(Folder(tree, "start").Children!, "to-b");
        Assert.Empty(Folder(toB.Children!, "to-a").Children!);
    }
}
