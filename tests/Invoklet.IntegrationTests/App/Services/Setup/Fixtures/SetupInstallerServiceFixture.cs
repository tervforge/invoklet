using System.Formats.Tar;
using System.IO.Compression;
using Invoklet.Services.Setup;
using Moq;

namespace Invoklet.IntegrationTests;

// The installer over a fake network and a fake process runner, extracting into a folder of its own that the fixture removes: never the
// user's own installation. What the network answers is set per test.
internal sealed class SetupInstallerServiceFixture : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-setup-tests-").FullName;

    public Mock<ISetupDownloader> Downloader { get; } = new();

    public Mock<ISetupProcessRunner> ProcessRunner { get; } = new();

    public string NetcoredbgFolder => Path.Combine(_root, "netcoredbg");

    public List<Uri> Downloaded { get; } = [];

    // The arguments the install script was run with.
    public List<string> ScriptArguments { get; } = [];

    public SetupInstallPlan SdkPlan(string? version) => new(".NET SDK", version, Path.Combine(_root, "dotnet"), "Microsoft (dot.net)", false);

    public SetupInstallPlan DebuggerPlan(string? tag) => new("netcoredbg", tag, NetcoredbgFolder, "GitHub (Samsung/netcoredbg)", false);

    /// <summary>Every download is answered with <paramref name="write"/>.</summary>
    public void NetworkAnswers(Action<string> write) =>
        Downloader.Setup(d => d.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns((Uri url, string destination, IProgress<double>? _, CancellationToken _) =>
            {
                Downloaded.Add(url);
                write(destination);
                return Task.CompletedTask;
            });

    public void NetworkAnswersText(string text)
    {
        NetworkAnswers(destination => File.WriteAllText(destination, text));
        Downloader.Setup(d => d.ReadTextAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(text);
    }

    public void NetworkFails()
    {
        Downloader.Setup(d => d.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));
        Downloader.Setup(d => d.ReadTextAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));
    }

    // The install script ends with the code, after making the dotnet it would have made in the plan's folder.
    public void ScriptEndsWith(int exitCode, SetupInstallPlan plan) =>
        ProcessRunner.Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> arguments, IProgress<string> _, CancellationToken _) =>
            {
                ScriptArguments.AddRange(arguments);
                Directory.CreateDirectory(plan.Folder);
                File.WriteAllText(Path.Combine(plan.Folder, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), "");
                return Task.FromResult(exitCode);
            });

    // A debugger already installed, to see what a failed update leaves.
    public string InstallWorkingDebugger()
    {
        Directory.CreateDirectory(NetcoredbgFolder);
        var installed = Path.Combine(NetcoredbgFolder, "netcoredbg-installed");
        File.WriteAllText(installed, "working");
        return installed;
    }

    public SetupInstallerService CreateSut() => new(Downloader.Object, ProcessRunner.Object, NetcoredbgFolder);

    // A release archive as netcoredbg publishes them: everything under a top-level "netcoredbg/" folder, zip or tar.gz by the asset
    // this system downloads.
    public static void WriteNetcoredbgArchive(string destination) => WriteArchive(destination, "netcoredbg/" + (OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg"));

    public static void WriteArchiveWithoutADebugger(string destination) => WriteArchive(destination, "netcoredbg/readme.txt");

    private static void WriteArchive(string destination, string entryName)
    {
        var content = "fake netcoredbg binary"u8.ToArray();
        if (destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
            using var stream = zip.CreateEntry(entryName).Open();
            stream.Write(content);
            return;
        }
        using var file = File.Create(destination);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip);
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = new MemoryStream(content) });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
