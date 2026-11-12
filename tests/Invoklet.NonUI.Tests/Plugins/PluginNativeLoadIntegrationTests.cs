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
        var files = PluginFiles.Read(_fixture.Root);
        var verifiedPath = files.GetNativeLoadPath(library);
        var replacement = Path.Combine(_fixture.Root, "replacement");
        File.WriteAllText(replacement, "not a native library");
        File.Move(replacement, library, overwrite: true);

        var handle = NativeLibrary.Load(verifiedPath);
        try
        {
            Assert.NotEqual(library, verifiedPath);
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
        var files = PluginFiles.Read(_fixture.Root);
        File.AppendAllText(library, "changed after admission");

        var error = Assert.Throws<IOException>(() => files.GetNativeLoadPath(library));

        Assert.Contains("changed before native loading", error.Message);
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
