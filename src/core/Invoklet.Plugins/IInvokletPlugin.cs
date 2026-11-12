namespace Invoklet.Plugins;

/// <summary>
/// The entry point of a plugin with code: the type the manifest's <c>entry.type</c> names. It has a public parameterless constructor,
/// and the host creates one instance per start. The role contracts (a connection plugin, a language plugin) are further interfaces
/// on the same type; the host asks for each role the manifest declares.
/// </summary>
public interface IInvokletPlugin
{
    PluginDescriptor Descriptor { get; }

    /// <summary>
    /// Called once, at start-up, after the manifest and descriptor were accepted and before any role is registered. Keep it
    /// deterministic and quick: no user interface, no network, no work on the caller's thread beyond wiring. Throwing rejects the
    /// plugin.
    /// </summary>
    void Initialize(PluginContext context) { }
}
