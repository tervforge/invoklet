using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Moq;

namespace Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;

// The schemas of one connection, kept in a folder of the fixture's own (removed with it), read through a driver whose answer the test
// sets: a schema with a summary, or a failure.
internal sealed class ConnectionSchemaCacheRefreshFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-schema-cache").FullName;

    public Mock<IConnectionSchemaDriver> Driver { get; } = new();

    public MdRunConnection Connection { get; } = new()
    {
        Name = "stock",
        Driver = "fake",
        Params = new(StringComparer.Ordinal) { ["address"] = MdRunValue.Of("http://localhost:5000") },
    };

    public ConnectionSchemaCacheRefreshFixture()
    {
        Driver.SetupGet(d => d.Id).Returns("fake");
        Driver.Setup(d => d.Validate(It.IsAny<MdRunConnection>())).Returns([]);
        ServerDescribes("1 service");
    }

    public void ServerDescribes(string summary) =>
        Driver.Setup(d => d.FetchSchemaAsync(It.IsAny<IReadOnlyDictionary<string, MdRunValue>>(), It.IsAny<ConnectionTools>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionSchema(summary, [], ""));

    public void ServerFails(string why) =>
        Driver.Setup(d => d.FetchSchemaAsync(It.IsAny<IReadOnlyDictionary<string, MdRunValue>>(), It.IsAny<ConnectionTools>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(why));

    public ConnectionSchemas CreateSut()
    {
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(Driver.Object);
        return new ConnectionSchemas(drivers, _folder, new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") }, _ => null);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
