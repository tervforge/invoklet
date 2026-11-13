using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Invoklet.Plugins;

namespace SampleHttpConnector;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("example.connector.http", "1.0.0");

    public void Register(ConnectionPluginContext context) => context.RegisterDriver(new SampleDriver());
}

internal sealed class SampleDriver : IConnectionDriver, ICSharpClientSource
{
    public string Id => "sample-http";
    public string DisplayName => "Sample HTTP";
    public ConnectionCategory Category => ConnectionCategory.Http;
    public IReadOnlyList<ConnectionParam> Params { get; } = [new("baseAddress", "Base address", Required: true)];
    public IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>();

    public IReadOnlyList<string> Validate(MdRunConnection connection) =>
        connection.Params.TryGetValue("baseAddress", out var address) && !string.IsNullOrWhiteSpace(address.Text)
            ? [] : ["The base address is missing."];

    public async Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams,
        CancellationToken cancellationToken)
    {
        if (!resolvedParams.TryGetValue("baseAddress", out var value)
            || !Uri.TryCreate(value.Text, UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
            return new(false, "An HTTP or HTTPS base address is required.");

        using var client = new HttpClient();
        try
        {
            using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException)
        {
            return new(false, "The endpoint could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "The endpoint did not respond in time.");
        }
    }

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context) => new($$"""
        public sealed class {{context.ClassName}} : global::System.IDisposable
        {
            public global::System.Net.Http.HttpClient Http { get; }

            internal {{context.ClassName}}()
            {
                Http = new global::System.Net.Http.HttpClient { BaseAddress = new global::System.Uri({{context.RuntimeText(connection.Params["baseAddress"].Text!)}}) };
            }

            public void Dispose() => Http.Dispose();
        }
        """);
}
