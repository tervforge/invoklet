using System.IO.Compression;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;

namespace Invoklet.IntegrationTests;

/// <summary>A temporary user plugin folder and inventory with theme packages installed through the real installer.</summary>
public sealed class PluginPackageInstallerFixture : IDisposable
{
    public const string Parent = "example.theme.ocean";
    public const string Child = "example.theme.reef";

    private readonly string _root = Directory.CreateTempSubdirectory("Invoklet-installer-").FullName;
    private string UserDirectory => Path.Combine(_root, "plugins");

    public PluginInventoryStore Store => new(Path.Combine(_root, PluginInventory.FileName));

    public PluginPackageInstaller CreateSut() => new(UserDirectory, Store, BundledPluginCatalog.Empty);

    public string InstalledDirectory(string id) => Path.Combine(UserDirectory, id, "1.0.0");

    public void Install(string id, string name, string? dependsOn = null)
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source", id)).FullName;
        var dependency = dependsOn is null ? "" : $$""","dependsOn":["{{dependsOn}}"]""";
        File.WriteAllText(Path.Combine(source, PluginContracts.ManifestFileName), $$"""
            {"id":"{{id}}","version":"1.0.0","displayName":"{{name}}","roles":{"theme":1},"assets":{"themes":["theme.json"]}{{dependency}}}
            """);
        File.WriteAllText(Path.Combine(source, "theme.json"), $$"""{"id":"{{id}}","name":"{{name}}"}""");
        var installer = CreateSut();
        var path = source + ".nupkg";
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var file in Directory.EnumerateFiles(source))
                archive.CreateEntryFromFile(file, "plugin/" + Path.GetFileName(file));
        using var package = installer.PrepareArchive(path);
        installer.Install(package);
    }

    /// <summary>Edits a file of an installed package after it was approved.</summary>
    public void ChangeInstalled(string id) => File.AppendAllText(Path.Combine(InstalledDirectory(id), "theme.json"), " ");

    public void CorruptInstalledManifest(string id) =>
        File.WriteAllText(Path.Combine(InstalledDirectory(id), PluginContracts.ManifestFileName), "{");

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
