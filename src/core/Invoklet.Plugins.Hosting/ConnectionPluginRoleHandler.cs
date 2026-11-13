using Invoklet.Contracts.Connections;

namespace Invoklet.Plugins.Hosting;

/// <summary>Collects the drivers declared by a plugin and applies them as a single registration.</summary>
public sealed class ConnectionPluginRoleHandler(ConnectionDriverRegistry registry) : IPluginRoleHandler
{
    public PluginRole Role => PluginRole.Connection;
    public int ContractVersion => PluginContracts.Connection;

    public PluginRoleRegistration Prepare(PluginRegistration registration)
    {
        if (registration.Plugin is not IConnectionPlugin plugin)
            throw new PluginRegistrationException("The entry point must implement IConnectionPlugin.");

        var drivers = new List<IConnectionDriver>();
        plugin.Register(new ConnectionPluginContext(registration.Context.Logger, drivers.Add));
        var pending = drivers.Select(driver => (Id: driver.Id, Driver: driver)).ToArray();
        var applied = 0;
        return new PluginRoleRegistration(
            () =>
            {
                foreach (var (_, driver) in pending)
                {
                    registry.Register(driver);
                    applied++;
                }
            },
            () =>
            {
                while (applied > 0)
                {
                    var (id, driver) = pending[--applied];
                    registry.Unregister(id, driver);
                }
            });
    }
}
