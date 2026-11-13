using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.Http;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.http", "1.0.0");

    public void Register(ConnectionPluginContext context)
    {
        context.RegisterDriver(new HttpConnectionDriver());
        context.RegisterDriver(new OpenApiConnectionDriver());
    }
}
