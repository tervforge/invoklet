namespace Invoklet.Contracts.Connections;

/// <summary>The connection drivers Invoklet knows, by <see cref="IConnectionDriver.Id"/> (letter case aside).</summary>
public sealed class ConnectionDriverRegistry(IReadOnlyList<string>? preferredOrder = null)
{
    private readonly Dictionary<string, IConnectionDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IConnectionDriver> _order = [];
    private readonly Dictionary<string, int> _ranks = (preferredOrder ?? [])
        .Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.OrdinalIgnoreCase);

    /// <summary>Preferred drivers first, then the rest in registration order.</summary>
    public IReadOnlyList<IConnectionDriver> Drivers => _order.OrderBy(driver => _ranks.GetValueOrDefault(driver.Id, int.MaxValue)).ToArray();

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

    public static string MissingDriverMessage(string driverId) => $"Driver '{driverId}' is not installed or is disabled.";

    internal void Unregister(string id, IConnectionDriver driver)
    {
        if (!ReferenceEquals(Find(id), driver)) return;
        _drivers.Remove(id);
        _order.RemoveAt(_order.FindIndex(item => ReferenceEquals(item, driver)));
    }
}
