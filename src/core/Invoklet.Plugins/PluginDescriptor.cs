namespace Invoklet.Plugins;

/// <summary>
/// What a plugin's code says about itself. It must agree with the manifest beside it: the manifest is what the host trusted and
/// hashed, and a mismatch means the folder holds something other than what was approved.
/// </summary>
/// <param name="Id">The stable id, such as <c>invoklet.connector.postgres</c>; never reused for a different plugin.</param>
/// <param name="Version">The plugin's own version, as in the manifest.</param>
public sealed record PluginDescriptor(string Id, string Version);
