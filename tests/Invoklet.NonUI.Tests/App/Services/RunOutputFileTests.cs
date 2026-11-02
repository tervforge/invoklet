using Invoklet.Services;
using Invoklet.UserControls.Models;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public class RunOutputFileTests
{
    [Fact]
    public void Save_OnUnix_Should_LeaveTheFileReadableByItsOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            RunOutputFile.Save(folder, new TabOutputState("token", false, new Dictionary<string, string>()));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder, "run-output.json")));
            Assert.Equal("token", RunOutputFile.TryLoad(folder)?.Content);
        }
        finally { Directory.Delete(folder, true); }
    }
}
