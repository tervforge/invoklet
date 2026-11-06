using Invoklet.Format.MdRun;

namespace Invoklet.Contracts.Connections;

/// <summary>The NuGet packages a script's connections put in it, kept in step with its target framework.</summary>
public static class ConnectionPackages
{
    /// <summary>
    /// The packages to change when the script moves from <paramref name="oldFramework"/> to <paramref name="newFramework"/>: a package of
    /// a connection's driver that is still at the release the driver gives the old framework moves to the one it gives the new framework
    /// (EF Core 10 on net10.0, EF Core 8 on net8.0). A version chosen by hand stays.
    /// </summary>
    /// <param name="oldFramework">A <c>targetSdk</c> such as <c>net8.0</c> or <c>latest</c>.</param>
    public static IReadOnlyDictionary<string, string> Retarget(IEnumerable<MdRunConnection> connections, ConnectionDriverRegistry drivers,
        IReadOnlyDictionary<string, Package> packages, string? oldFramework, string? newFramework)
    {
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in connections.Select(c => drivers.Find(c.Driver)).OfType<IConnectionDriver>().Distinct())
        {
            var before = driver.PackagesFor(oldFramework);
            foreach (var (id, version) in driver.PackagesFor(newFramework))
            {
                if (packages.TryGetValue(id, out var current) && before.TryGetValue(id, out var old)
                    && string.Equals(current.Version, old, StringComparison.OrdinalIgnoreCase) && !string.Equals(version, old, StringComparison.OrdinalIgnoreCase))
                    changes[id] = version;
            }
        }
        return changes;
    }
}
