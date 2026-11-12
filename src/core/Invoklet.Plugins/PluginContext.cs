using Microsoft.Extensions.Logging;

namespace Invoklet.Plugins;

/// <summary>
/// What a plugin may know about where it runs. Deliberately narrow: no service provider, windows, preferences, secrets or other
/// plugins. A role adds its own registration API in its own context type; this is what every role shares.
/// </summary>
public sealed class PluginContext
{
    public PluginContext(PluginManifest manifest, string directory, PluginTrust trust, ILogger logger)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Trust = trust;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The manifest the host accepted; its id is the plugin's.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>The folder of the manifest, for a plugin's own data files.</summary>
    public string Directory { get; }

    public PluginTrust Trust { get; }

    /// <summary>A logger whose category names the plugin.</summary>
    public ILogger Logger { get; }
}
