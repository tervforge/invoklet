using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

/// <summary>A Plugins page over one plugin rejected because its contents changed, with the inventory held in memory.</summary>
public sealed class PluginsPageViewModelFixture
{
    public Mock<Func<PluginPackageInstaller>> CreateInstaller { get; } = new();

    /// <param name="source">The inventory source of the plugin's entry; null leaves the inventory empty.</param>
    public PluginsPageViewModel CreateSut(PluginTrust trust, string? source)
    {
        var manifest = PluginManifestReader.Read("""
            {"id":"example.theme.ocean","version":"1.0.0","displayName":"Ocean","roles":{"theme":1}}
            """).Manifest!;
        var diagnostic = PluginDiagnostic.Error(manifest.Id, PluginDiagnosticCode.PluginContentChanged,
            "The contents of 'example.theme.ocean' changed since they were approved.");
        var session = new PluginSession([], [new RejectedPlugin(manifest.Id, manifest, "plugins/example.theme.ocean/1.0.0", trust, [diagnostic])]);
        var inventory = source is null ? PluginInventory.Empty : new PluginInventory([new PluginInventoryEntry(manifest.Id, manifest.Version,
            ["theme"], source, null, new string('a', 64), "none", Enabled: true, Approved: true)]);
        return new PluginsPageViewModel(session, () => inventory, () => Task.CompletedTask, createInstaller: CreateInstaller.Object);
    }
}
