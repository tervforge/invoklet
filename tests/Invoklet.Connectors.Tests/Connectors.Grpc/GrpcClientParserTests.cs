using Invoklet.Connectors.Grpc;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.Grpc;

[Trait("Platform", "Independent")]
public sealed class GrpcClientParserTests
{
    // A service's property is its name, with its package only where two services share a name; a lowercase name starts with a capital.
    [Fact]
    public void WithProperties_ServicesOfTheSameName_GetShortUniqueProperties()
    {
        // Arrange
        GrpcServiceClient[] clients =
        [
            new("inventory.v1.Stock", "global::A.Stock.StockClient", ""),
            new("legacy.Stock", "global::B.Stock.StockClient", ""),
            new("inventory.v1.Audit", "global::A.Audit.AuditClient", ""),
            new("x.channel", "global::C.channel.channelClient", ""),
        ];

        // Act
        var named = GrpcClientParser.WithProperties(clients);

        // Assert
        Assert.Equal(["InventoryV1Stock", "LegacyStock", "Audit", "XChannel"], named.Select(c => c.Property));
    }
}
