namespace Invoklet.Contracts.Connections;

/// <summary>The connection drivers Invoklet knows, by <see cref="IConnectionDriver.Id"/> (letter case aside).</summary>
public sealed class ConnectionDriverRegistry
{
    private readonly Dictionary<string, IConnectionDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IConnectionDriver> _order = [];

    /// <summary>In registration order.</summary>
    public IReadOnlyList<IConnectionDriver> Drivers => [.. _order];

    /// <summary>
    /// A driver id can be registered once: a second registration throws, so a plugin can never replace a built-in driver or
    /// another plugin's. The plugin loader turns the exception into that plugin's diagnostic.
    /// </summary>
    /// <exception cref="InvalidOperationException">A driver with the same id (letter case aside) is registered.</exception>
    public void Register(IConnectionDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (!_drivers.TryAdd(driver.Id, driver))
            throw new InvalidOperationException($"A connection driver '{driver.Id}' is already registered.");
        _order.Add(driver);
    }

    /// <summary>Null for a driver this Invoklet doesn't have (a connection from a newer Invoklet or a missing plugin).</summary>
    public IConnectionDriver? Find(string driverId) => _drivers.GetValueOrDefault(driverId);
}
