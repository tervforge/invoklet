using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.Grpc;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.grpc", "1.0.0");

    public void Register(ConnectionPluginContext context)
    {
        context.RegisterDriver(new GrpcConnectionDriver());
    }
}
