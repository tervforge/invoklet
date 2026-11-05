using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Grpc.Reflection.V1Alpha;
using Inventory.V1;
using Invoklet.Format.MdRun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Grpc.Fixtures;

/// <summary>
/// Real gRPC servers on loopback ports, started once for a test class: one with server reflection (Stock and Audit), one without it.
/// Servers of a kind of their own (HTTPS, HTTP/1, a picky reflection) are started by the tests that need them.
/// </summary>
public sealed class GrpcServersFixture : IAsyncLifetime
{
    private WebApplication? _server;
    private WebApplication? _plainServer;

    /// <summary>The server with reflection.</summary>
    public string Address { get; private set; } = "";

    /// <summary>The server without reflection, for the error a user sees then.</summary>
    public string PlainAddress { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        (_server, Address) = await StartAsync(reflection: true);
        (_plainServer, PlainAddress) = await StartAsync(reflection: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
        if (_plainServer is not null) await _plainServer.DisposeAsync();
    }

    public static Dictionary<string, MdRunValue> Params(string address, bool skipCertificate = false)
    {
        var parameters = new Dictionary<string, MdRunValue>(StringComparer.Ordinal) { ["address"] = MdRunValue.Of(address) };
        if (skipCertificate) parameters["skipCertificateCheck"] = MdRunValue.Of("true");
        return parameters;
    }

    public MdRunConnection Inventory(string? address = null, string? metadata = "Bearer ${secret:inventory-token}") => new()
    {
        Name = "inventory",
        Driver = "grpc",
        Params = metadata is null
            ? new(StringComparer.Ordinal) { ["address"] = MdRunValue.Of(address ?? Address) }
            : new(StringComparer.Ordinal)
            {
                ["address"] = MdRunValue.Of(address ?? Address),
                ["metadata"] = MdRunValue.Map([new("authorization", MdRunValue.Of(metadata))]),
            },
    };

    public static async Task<(WebApplication Server, string Address)> StartAsync(bool reflection, HttpProtocols protocols = HttpProtocols.Http2,
        X509Certificate2? certificate = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l =>
        {
            l.Protocols = protocols;
            if (certificate is not null) l.UseHttps(certificate);
        }));
        builder.Services.AddGrpc();
        if (reflection) builder.Services.AddGrpcReflection();
        var app = builder.Build();
        if (!reflection)
        {
            app.Use(async (context, next) =>
            {
                // Let reflection requests finish writing before Kestrel rejects the unknown service.
                if (context.Request.Path.Value?.StartsWith("/grpc.reflection.", StringComparison.Ordinal) == true)
                    await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
                await next(context);
            });
        }
        app.MapGrpcService<StockService>();
        app.MapGrpcService<AuditService>();
        if (reflection) app.MapGrpcReflectionService();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, address.Replace("[::1]", "127.0.0.1", StringComparison.Ordinal));
    }

    /// <summary>Like the Postman Echo server: reflection in the v1alpha protocol only, and a list request whose list_services is empty is never answered.</summary>
    public static async Task<(WebApplication Server, string Address)> StartPickyAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        var app = builder.Build();
        app.MapGrpcService<PickyReflection>();
        await app.StartAsync();
        return (app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public static X509Certificate2 LoopbackCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    /// <summary>A handler that sends every connection to a loopback port, whatever the address names: how
    /// "http://grpc.postman-echo.com:443" reaches a local server without the network or port 443.</summary>
    public static Func<HttpMessageHandler> Redirect(string address) => () => new SocketsHttpHandler
    {
        ConnectCallback = async (_, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, new Uri(address).Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private sealed class StockService : Stock.StockBase
    {
        public override Task<Item> GetItem(ItemRequest request, ServerCallContext context) => Task.FromResult(new Item
        {
            Sku = request.Sku,
            Count = 42,
            Caller = string.Join(",", context.RequestHeaders.Where(h => h.Key == "authorization").Select(h => h.Value)),
        });
    }

    private sealed class AuditService : Audit.AuditBase
    {
        public override Task<ItemRequest> Ping(ItemRequest request, ServerCallContext context) => Task.FromResult(request);
    }

    private sealed class PickyReflection : ServerReflection.ServerReflectionBase
    {
        public override async Task ServerReflectionInfo(IAsyncStreamReader<ServerReflectionRequest> requestStream,
            IServerStreamWriter<ServerReflectionResponse> responseStream, ServerCallContext context)
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (request.ListServices != "*") continue;
                var response = new ServerReflectionResponse { OriginalRequest = request, ListServicesResponse = new ListServiceResponse() };
                response.ListServicesResponse.Service.Add(new ServiceResponse { Name = "grpc.reflection.v1alpha.ServerReflection" });
                response.ListServicesResponse.Service.Add(new ServiceResponse { Name = "HelloService" });
                await responseStream.WriteAsync(response);
            }
        }
    }
}
