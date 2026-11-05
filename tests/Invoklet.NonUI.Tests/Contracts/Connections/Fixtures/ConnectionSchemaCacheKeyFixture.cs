using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Moq;

namespace Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;

// A cache whose keys are compared, with a driver that says which version of its descriptions it writes. Nothing is read or written.
internal sealed class ConnectionSchemaCacheKeyFixture
{
    public Mock<IConnectionSchemaDriver> Driver { get; } = new();

    public ConnectionSchemaCacheKeyFixture() => Driver.SetupGet(d => d.Id).Returns("fake");

    public static MdRunConnection Connection() => new()
    {
        Name = "inventory",
        Driver = "fake",
        Params = new(StringComparer.Ordinal) { ["address"] = MdRunValue.Of("https://inventory.local") },
    };

    public ConnectionSchemaCache CreateSut(string schemaVersion)
    {
        Driver.SetupGet(d => d.SchemaVersion).Returns(schemaVersion);
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(Driver.Object);
        return new ConnectionSchemaCache("schemas", drivers);
    }
}
