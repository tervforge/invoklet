using Invoklet.Plugins;
using Microsoft.Extensions.Logging;

namespace Invoklet.Contracts.Connections;

/// <summary>A plugin that supplies drivers for script connections.</summary>
public interface IConnectionPlugin : IInvokletPlugin
{
    void Register(ConnectionPluginContext context);
}

/// <summary>Stages driver registrations for the host and exposes the plugin's logger.</summary>
public sealed class ConnectionPluginContext
{
    private readonly Action<IConnectionDriver> _register;

    public ConnectionPluginContext(ILogger logger, Action<IConnectionDriver> register)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _register = register ?? throw new ArgumentNullException(nameof(register));
    }

    public ILogger Logger { get; }

    public void RegisterDriver(IConnectionDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _register(driver);
    }
}

/// <summary>Identifies the target platform when a host prepares a connection.</summary>
public interface IPlatformConnectionContext
{
    string PlatformId { get; }
}
