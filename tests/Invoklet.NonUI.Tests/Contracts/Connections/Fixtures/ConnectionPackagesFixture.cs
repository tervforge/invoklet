using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Moq;

namespace Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;

// A database driver whose package has a release per framework, as EF Core's providers do, and a script with one connection of it.
internal sealed class ConnectionPackagesFixture
{
    public const string Package = "Provider.EntityFrameworkCore";

    public Mock<IConnectionDriver> Driver { get; } = new();

    public MdRunConnection Connection { get; } = new() { Name = "shop", Driver = "db", Params = new(StringComparer.Ordinal) };

    public ConnectionPackagesFixture()
    {
        Driver.SetupGet(d => d.Id).Returns("db");
        Driver.Setup(d => d.PackagesFor("net8.0")).Returns(new Dictionary<string, string> { [Package] = "8.0.11" });
        Driver.Setup(d => d.PackagesFor("latest")).Returns(new Dictionary<string, string> { [Package] = "10.0.3" });
    }

    public static Dictionary<string, Package> ScriptPackages(string? version) =>
        version is null ? [] : new() { [Package] = new Package(version) };

    public ConnectionDriverRegistry Drivers()
    {
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(Driver.Object);
        return drivers;
    }
}
