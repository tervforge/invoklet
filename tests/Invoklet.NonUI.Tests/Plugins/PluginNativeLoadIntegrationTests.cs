using System.Runtime.InteropServices;
using Invoklet.NonUI.Tests.Plugins.Fixtures;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Category", "Integration")]
public sealed class PluginNativeLoadIntegrationTests : IDisposable
{
    private readonly PluginLoaderFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void GetNativeLoadPath_OriginalIsReplacedBeforeNativeLoad_LoadsTheVerifiedCopy()
    {
        var library = CopyNativeLibrary();
        var files = PluginFiles.Read(_fixture.Root, _fixture.NativeCache);
        var verifiedPath = files.GetNativeLoadPath(library);
        var replacement = Path.Combine(_fixture.Root, "replacement");
        File.WriteAllText(replacement, "not a native library");
        File.Move(replacement, library, overwrite: true);

        var handle = NativeLibrary.Load(verifiedPath);
        try
        {
            Assert.NotEqual(library, verifiedPath);
            Assert.StartsWith(_fixture.NativeCache, verifiedPath, StringComparison.Ordinal);
            Assert.NotEqual(IntPtr.Zero, NativeLibrary.GetExport(handle, "sqlite3_libversion_number"));
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    [Fact]
    public void GetNativeLoadPath_ApprovedFileChangesBeforeSnapshot_RejectsTheFile()
    {
        var library = CopyNativeLibrary();
        var files = PluginFiles.Read(_fixture.Root, _fixture.NativeCache);
        File.AppendAllText(library, "changed after admission");

        var error = Assert.Throws<IOException>(() => files.GetNativeLoadPath(library));

        Assert.Contains("changed before native loading", error.Message);
        Assert.Empty(Directory.GetDirectories(_fixture.NativeCache));
    }

    [Fact]
    public void GetNativeLoadPath_LaterStartWithTheSameContent_ReusesTheCopy()
    {
        var library = CopyNativeLibrary();
        var firstStart = PluginFiles.Read(_fixture.Root, _fixture.NativeCache).GetNativeLoadPath(library);
        var written = File.GetLastWriteTimeUtc(firstStart);

        var secondStart = PluginFiles.Read(_fixture.Root, _fixture.NativeCache).GetNativeLoadPath(library);

        Assert.Equal(firstStart, secondStart);
        Assert.Equal(written, File.GetLastWriteTimeUtc(secondStart));
        Assert.Single(Directory.GetDirectories(_fixture.NativeCache));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetNativeLoadPath_CachedCopyChanged_MakesAFreshVerifiedCopy(bool addedFile)
    {
        var library = CopyNativeLibrary();
        var original = File.ReadAllBytes(library);
        var cached = PluginFiles.Read(_fixture.Root, _fixture.NativeCache).GetNativeLoadPath(library);
        if (addedFile) File.WriteAllText(Path.Combine(Path.GetDirectoryName(cached)!, "unexpected.dll"), "not approved");
        else File.AppendAllText(cached, "changed in the cache");

        var fresh = PluginFiles.Read(_fixture.Root, _fixture.NativeCache).GetNativeLoadPath(library);

        Assert.Equal(original, File.ReadAllBytes(fresh));
        Assert.NotEqual(cached, fresh);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fresh)!, "unexpected.dll")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PruneNativeCache_UnusedCopies_AreRemovedWhileLeasedAndRecentOnesStay(bool keepActiveHash)
    {
        var library = CopyNativeLibrary();
        var files = PluginFiles.Read(_fixture.Root, _fixture.NativeCache);
        var kept = Path.GetDirectoryName(files.GetNativeLoadPath(library))!;
        var stale = Directory.CreateDirectory(Path.Combine(_fixture.NativeCache, new string('0', 64))).FullName;
        File.WriteAllText(Path.Combine(stale, "old.dll"), "gone plugin");
        var inProgress = Directory.CreateDirectory(Path.Combine(_fixture.NativeCache, new string('1', 64) + ".abc.tmp")).FullName;

        PluginFiles.PruneNativeCache(_fixture.NativeCache, keepActiveHash ? new HashSet<string> { files.ContentHash } : new HashSet<string>());

        Assert.True(Directory.Exists(kept));
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(inProgress));
    }

    private string CopyNativeLibrary()
    {
        // The SQLite driver already brings a native library for each desktop platform.
        var name = OperatingSystem.IsWindows() ? "e_sqlite3.dll" : OperatingSystem.IsMacOS() ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
        var source = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name);
        var directory = Directory.CreateDirectory(Path.Combine(_fixture.Root, "native"));
        var path = Path.Combine(directory.FullName, name);
        File.Copy(source, path);
        return path;
    }
}
