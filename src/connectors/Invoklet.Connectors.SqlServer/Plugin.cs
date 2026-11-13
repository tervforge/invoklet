using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.SqlServer;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.sqlserver", "1.0.0");

    public void Register(ConnectionPluginContext context)
    {
        context.RegisterDriver(new SqlServerConnectionDriver());
    }
}
