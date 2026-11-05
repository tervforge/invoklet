using System.Text.Json;
using Invoklet.Connectors.Grpc;
using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Moq;

namespace Invoklet.Connectors.Tests.Connectors.Grpc.Fixtures;

// A gRPC driver, the connections it is given and the schemas its generated client is written from (no server, no cache on disk).
internal sealed class GrpcConnectionDriverFixture
{
    public Mock<IConnectionSchemaSource> Schemas { get; } = new();

    public GrpcConnectionDriver CreateSut() => new();

    public static MdRunConnection Connection(string address = "https://inventory.local:5001", string? authorization = null,
        string? skipCertificateCheck = null, string name = "inventory")
    {
        var parameters = new Dictionary<string, MdRunValue>(StringComparer.Ordinal) { ["address"] = MdRunValue.Of(address) };
        if (authorization is not null) parameters["metadata"] = MdRunValue.Map([new("authorization", MdRunValue.Of(authorization))]);
        if (skipCertificateCheck is not null) parameters["skipCertificateCheck"] = MdRunValue.Of(skipCertificateCheck);
        return new MdRunConnection { Name = name, Driver = GrpcConnectionDriver.DriverId, Params = parameters };
    }

    public static MdRunConnection WithMetadataName(string metadataName) => new()
    {
        Name = "inventory",
        Driver = GrpcConnectionDriver.DriverId,
        Params = new(StringComparer.Ordinal)
        {
            ["address"] = MdRunValue.Of("https://inventory.local:5001"),
            ["metadata"] = MdRunValue.Map([new(metadataName, MdRunValue.Of("x"))]),
        },
    };

    // The parameters a Test gets: an address nothing listens on, which an invalid parameter keeps it from ever reaching.
    public static Dictionary<string, MdRunValue> TestParams(string metadataName) => new(StringComparer.Ordinal)
    {
        ["address"] = MdRunValue.Of("http://127.0.0.1:1"),
        ["metadata"] = MdRunValue.Map([new(metadataName, MdRunValue.Of("x"))]),
    };

    /// <summary>The schema read from the server names these services' clients.</summary>
    public void ServerHas(params (string Service, string ClientType, string Property)[] clients)
    {
        var data = JsonSerializer.Serialize(clients.Select(c => new { c.Service, c.ClientType, c.Property }));
        Schemas.Setup(s => s.Get(It.IsAny<MdRunConnection>(), It.IsAny<string?>())).Returns(new ConnectionSchema("", [], data));
    }

    public ConnectionDriverRegistry Drivers(GrpcConnectionDriver sut)
    {
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(sut);
        return drivers;
    }

    public IReadOnlyList<string> Packages(GrpcConnectionDriver sut) => [.. sut.Packages.Keys];
}
