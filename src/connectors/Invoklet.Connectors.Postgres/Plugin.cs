using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.Postgres;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.postgres", "1.0.0");

    public void Register(ConnectionPluginContext context)
    {
        context.RegisterDriver(new PostgresConnectionDriver());
    }
}
