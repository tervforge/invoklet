using Invoklet.Connectors.Grpc;
using Invoklet.Connectors.IntegrationTests.Connectors.Grpc.Fixtures;
using Invoklet.Contracts.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Grpc;

// The Test button of a gRPC connection against real servers on loopback ports: what it lists, and the words for each way it fails.
public sealed class GrpcServerTests(GrpcServersFixture servers) : IClassFixture<GrpcServersFixture>
{
    private const string BothServices = "2 services: inventory.v1.Audit, inventory.v1.Stock.";
    private const string TlsFailed = "The TLS handshake failed: if the server doesn't use TLS, use an http:// address.";

    [Fact]
    public async Task TestAsync_AServerWithReflection_ListsItsServices()
    {
        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(servers.Address), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ConnectionTestResult(true, BothServices), result);
    }

    [Fact]
    public async Task TestAsync_AServerWithoutReflection_SaysItIsNeeded()
    {
        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(servers.PlainAddress), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ConnectionTestResult(false, "The server doesn't offer gRPC server reflection, which Invoklet reads its services with."), result);
    }

    [Fact]
    public async Task TestAsync_NothingListening_SaysTheServerCanNotBeReached()
    {
        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params("http://127.0.0.1:1"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.StartsWith("Could not reach the server:", result.Message, StringComparison.Ordinal);
    }

    // Plaintext HTTP/2 on any port, and services asked for the way a server like Postman Echo answers.
    [Fact]
    public async Task TestAsync_APickyReflectionServer_ListsItsServices()
    {
        // Arrange
        var (server, address) = await GrpcServersFixture.StartPickyAsync();
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(address), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ConnectionTestResult(true, "1 service: HelloService."), result);
    }

    // The address of the Postman Echo server, http:// on port 443, sent to the local server.
    [Fact]
    public async Task TestAsync_PlaintextOnPort443_ListsTheServices()
    {
        // Arrange
        var (server, address) = await GrpcServersFixture.StartPickyAsync();
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver(GrpcServersFixture.Redirect(address))
            .TestAsync(GrpcServersFixture.Params("http://grpc.postman-echo.com:443"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ConnectionTestResult(true, "1 service: HelloService."), result);
    }

    [Fact]
    public async Task TestAsync_HttpsWithTheCertificateCheckSkipped_ListsTheServices()
    {
        // Arrange
        using var certificate = GrpcServersFixture.LoopbackCertificate();
        var (server, address) = await GrpcServersFixture.StartAsync(reflection: true, certificate: certificate);
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(address, skipCertificate: true),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ConnectionTestResult(true, BothServices), result);
    }

    [Fact]
    public async Task TestAsync_HttpsWithAnUntrustedCertificate_SaysTheHandshakeFailed()
    {
        // Arrange
        using var certificate = GrpcServersFixture.LoopbackCertificate();
        var (server, address) = await GrpcServersFixture.StartAsync(reflection: true, certificate: certificate);
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(address), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.StartsWith("Could not reach the server:", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(TlsFailed, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_AServerWithoutHttp2_SaysGrpcNeedsIt()
    {
        // Arrange
        var (server, address) = await GrpcServersFixture.StartAsync(reflection: true, HttpProtocols.Http1);
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(GrpcServersFixture.Params(address), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.EndsWith("The server doesn't speak HTTP/2, which gRPC needs: check the address and port, and use https:// if the server uses TLS.",
            result.Message, StringComparison.Ordinal);
    }

    // The HTTP/2 transport words this mismatch differently on each system; the hint names the https:// address either way.
    [Fact]
    public async Task TestAsync_PlaintextToATlsServer_PointsToHttps()
    {
        // Arrange
        using var certificate = GrpcServersFixture.LoopbackCertificate();
        var (server, address) = await GrpcServersFixture.StartAsync(reflection: true, certificate: certificate);
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(
            GrpcServersFixture.Params(address.Replace("https://", "http://", StringComparison.Ordinal)), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("https://", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_TlsToAPlaintextServer_SaysTheHandshakeFailed()
    {
        // Arrange
        var (server, address) = await GrpcServersFixture.StartAsync(reflection: true, HttpProtocols.Http1);
        await using var _ = server;

        // Act
        var result = await new GrpcConnectionDriver().TestAsync(
            GrpcServersFixture.Params(address.Replace("http://", "https://", StringComparison.Ordinal)), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.EndsWith(TlsFailed, result.Message, StringComparison.Ordinal);
    }
}
