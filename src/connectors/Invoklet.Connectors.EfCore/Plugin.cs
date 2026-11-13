using Invoklet.Plugins;

namespace Invoklet.Connectors.EfCore;

public sealed class Plugin : IInvokletPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.efcore", "1.0.0");
}
