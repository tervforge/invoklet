namespace Invoklet.Plugins;

/// <summary>
/// Where a plugin came from, which decides what Invoklet requires before loading it. None of these is a sandbox: a loaded plugin
/// runs with the rights of the Invoklet process, and the Plugins page says so.
/// </summary>
public enum PluginTrust
{
    /// <summary>Shipped with Invoklet in its own <c>plugins</c> folder and listed, with its hash, in the bundled inventory.</summary>
    Bundled,

    /// <summary>Installed by the user, approved by the user and unchanged since (its hash matches the inventory).</summary>
    Trusted,

    /// <summary>A folder the user pointed Invoklet at for developing a plugin; loaded without an inventory record and shown as such.</summary>
    Development,
}
