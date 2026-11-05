using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Moq;

namespace Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;

// A driver that reads a schema, the schemas it has read (in memory, no cache on disk) and the connections that use it.
internal sealed class GeneratedConnectionsFixture
{
    public const string DriverId = "fake";

    public Mock<IConnectionSchemaDriver> Driver { get; } = new();
    public Mock<IConnectionSchemaSource> Schemas { get; } = new();

    public GeneratedConnectionsFixture()
    {
        Driver.SetupGet(d => d.Id).Returns(DriverId);
        Driver.SetupGet(d => d.DisplayName).Returns("Fake");
        Driver.SetupGet(d => d.Packages).Returns(new Dictionary<string, string>());
        Driver.Setup(d => d.Validate(It.IsAny<MdRunConnection>())).Returns([]);
        Driver.Setup(d => d.CreateClient(It.IsAny<MdRunConnection>(), It.IsAny<ConnectionCodeContext>()))
            .Returns((MdRunConnection _, ConnectionCodeContext context) => new ConnectionClientCode($"public sealed class {context.ClassName} {{ }}"));
    }

    public static MdRunConnection Connection(string name) => new() { Name = name, Driver = DriverId, Params = new(StringComparer.Ordinal) };

    /// <summary>The schema read for <paramref name="connection"/> has these files.</summary>
    public void HasSchema(MdRunConnection connection, params GeneratedSourceFile[] files) =>
        Schemas.Setup(s => s.Get(connection, It.IsAny<string?>())).Returns(new ConnectionSchema("", files, "[]"));

    /// <summary>The last read of <paramref name="connection"/>'s schema failed, for this reason.</summary>
    public void CouldNotRead(MdRunConnection connection, string why) =>
        Schemas.Setup(s => s.ErrorFor(connection, It.IsAny<string?>())).Returns(why);

    public ConnectionDriverRegistry Drivers()
    {
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(Driver.Object);
        return drivers;
    }
}
