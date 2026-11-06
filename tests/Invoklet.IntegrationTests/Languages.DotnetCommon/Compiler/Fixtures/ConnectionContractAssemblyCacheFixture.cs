namespace Invoklet.IntegrationTests;

// A folder of contract files, as a build leaves them, removed with the fixture.
internal sealed class ConnectionContractAssemblyCacheFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("Invoklet-contracts-").FullName;

    // A contract and its documentation; the path of the assembly.
    public string AddContract(string key)
    {
        var path = Path.Combine(Folder, "Invoklet.ConnectionContracts." + key + ".dll");
        File.WriteAllText(path, "");
        File.WriteAllText(Path.ChangeExtension(path, ".xml"), "");
        return path;
    }

    // What a build writes before it moves its contract into place.
    public string AddContractOnItsWayIn(string key)
    {
        var path = Path.Combine(Folder, "Invoklet.ConnectionContracts." + key + ".dll.0123.tmp");
        File.WriteAllText(path, "");
        File.WriteAllText(path + ".xml", "");
        return path;
    }

    public string AddOtherFile(string name)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, "");
        return path;
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
