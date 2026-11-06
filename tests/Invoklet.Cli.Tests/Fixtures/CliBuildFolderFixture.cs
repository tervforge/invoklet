namespace Invoklet.Cli.Tests;

// A folder of builds of its own, with a script path in it, removed with the fixture.
internal sealed class CliBuildFolderFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Directory.CreateTempSubdirectory("Invoklet-cli-builds-").FullName, "builds");

    public string ScriptPath { get; } = Path.Combine(Path.GetTempPath(), "Invoklet-cli-fixture", "report.inv");

    public TimeSpan MaxAge { get; } = TimeSpan.FromDays(14);

    // A build folder a run left a month ago, with the lock file a run leaves beside it.
    public string AddUnusedFolder(string name)
    {
        var folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + ".lock", "");
        Directory.SetLastWriteTimeUtc(folder, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return folder;
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
}
