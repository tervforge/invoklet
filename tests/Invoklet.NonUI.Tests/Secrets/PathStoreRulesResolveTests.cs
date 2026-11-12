using Invoklet.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Secrets;

// How the value of a named path becomes the full path a script gets: the strings only, no file is read.
public sealed class PathStoreRulesResolveTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Resolve_AFullPath_IsKeptWithoutItsTrailingSeparator()
        => Assert.Equal(Path.Combine(Root, "data", "set"), PathStoreRules.Resolve(Path.Combine(Root, "data", "set") + Path.DirectorySeparatorChar));

    [Fact]
    public void Resolve_TheRoot_KeepsItsSeparator()
        => Assert.Equal(Root, PathStoreRules.Resolve(Root));

    [Fact]
    public void Resolve_DotSegments_AreTakenOut()
        => Assert.Equal(Path.Combine(Root, "a", "c"), PathStoreRules.Resolve(Path.Combine(Root, "a", "b", "..", ".", "c")));

    [Fact]
    public void Resolve_Tilde_IsTheHomeFolder()
        => Assert.Equal(Home, PathStoreRules.Resolve("~"));

    [Fact]
    public void Resolve_UnderTilde_IsUnderTheHomeFolder()
        => Assert.Equal(Path.Combine(Home, "work", "repo"), PathStoreRules.Resolve("~/work/repo"));

    [Fact]
    public void Resolve_ARelativePath_Throws()
        => Assert.Throws<ArgumentException>(() => PathStoreRules.Resolve(Path.Combine("..", "data")));

    [Theory]
    [InlineData("*.csv")]
    [InlineData("data?.csv")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a\"b")]
    [InlineData("one:two:three.txt")]
    [InlineData("file:stream:")]
    [InlineData("file:stream:$NO_SUCH_TYPE")]
    [InlineData("file:stream:$DATA:extra")]
    public void Resolve_AWindowsOnlyInvalidCharacter_IsRejectedOnWindowsAndKeptOnUnix(string name)
    {
        var path = Path.Combine(Root, name);

        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => PathStoreRules.Resolve(path));
        else
            Assert.Equal(path, PathStoreRules.Resolve(path));
    }

    [Theory]
    [InlineData(@"C:\dir\file.txt")]
    [InlineData(@"C:\dir\file.txt:notes")]
    [InlineData(@"C:\dir\file.txt:notes:$data")]
    [InlineData(@"C:\dir\file.txt::$DATA")]
    [InlineData(@"\\?\C:\dir\file.txt:notes:$DATA")]
    [InlineData(@"\\server\share\file.txt:notes")]
    [InlineData(@"C:\dir::$INDEX_ALLOCATION")]
    public void Resolve_AWindowsDriveOrStreamPath_IsAccepted(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path syntax.");

        Assert.Equal(path, PathStoreRules.Resolve(path));
    }

    [Theory]
    [InlineData(@"C:\dir:notes\file.txt")]
    [InlineData(@"C:\dir\:notes")]
    [InlineData(@"C:\dir\file.txt:")]
    public void Resolve_AMisplacedOrEmptyStreamSuffix_IsRejectedOnWindows(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path syntax.");

        Assert.Throws<ArgumentException>(() => PathStoreRules.Resolve(path));
    }

    [Fact]
    public void Resolve_AnExtendedWindowsPathWithForwardSlashes_IsNormalized()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = "//?/" + Path.Combine(Root, "data").Replace('\\', '/');

        Assert.Equal(Path.GetFullPath(path), PathStoreRules.Resolve(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a\0b")]
    public void Resolve_NoPath_Throws(string? value)
        => Assert.Throws<ArgumentException>(() => PathStoreRules.Resolve(value));
}
