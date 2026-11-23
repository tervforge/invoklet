using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Invoklet.IntegrationTests;

/// <summary>
/// A NuGet v3 feed of plugin packages held in memory and served over HTTP on the loopback address: the service index, a search
/// that returns every package, and the package base address (versions and downloads).
/// </summary>
public sealed class StaticPluginFeed : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, SortedDictionary<string, byte[]>> _packages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tags = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();

    public StaticPluginFeed()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseAddress = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseAddress);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string BaseAddress { get; }
    public string IndexUrl => BaseAddress + "v3/index.json";

    /// <summary>Every request answers 401, as GitHub Packages does without credentials.</summary>
    public bool RequireCredentials { get; set; }

    /// <summary>Adds a theme package whose manifest is <paramref name="manifest"/>, under <c>plugin/</c>.</summary>
    public void AddTheme(string id, string version, string manifest, string tags = "invoklet plugin")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, id + ".nuspec", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{id}</id><version>{version}</version><authors>Example</authors><description>Theme {id}.</description><tags>{tags}</tags></metadata>
                </package>
                """);
            Write(archive, "plugin/invoklet-plugin.json", manifest);
            Write(archive, "plugin/theme.json", $$"""{"id":"{{id}}","name":"Ocean"}""");
        }
        if (!_packages.TryGetValue(id, out var versions)) _packages[id] = versions = new(StringComparer.Ordinal);
        versions[version] = stream.ToArray();
        _tags[id] = tags;
    }

    /// <summary>Adds a packed <c>.nupkg</c> as it is, with the id, version and tags its nuspec gives.</summary>
    public void AddPackage(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var nuspec = archive.Entries.Single(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        var metadata = System.Xml.Linq.XDocument.Load(stream).Root!.Elements().Single(element => element.Name.LocalName == "metadata");
        string Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? "";
        var id = Value("id");
        if (!_packages.TryGetValue(id, out var versions)) _packages[id] = versions = new(StringComparer.Ordinal);
        versions[Value("version")] = package;
        _tags[id] = Value("tags");
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(text));
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            using var response = context.Response;
            var (status, body, type) = Answer(context.Request.Url!.AbsolutePath);
            response.StatusCode = status;
            response.ContentType = type;
            await response.OutputStream.WriteAsync(body);
        }
    }

    private (int Status, byte[] Body, string Type) Answer(string path)
    {
        if (RequireCredentials) return (401, [], "text/plain");
        var segments = path.Trim('/').Split('/');
        object? json = segments switch
        {
            ["v3", "index.json"] => new
            {
                version = "3.0.0",
                resources = new object[]
                {
                    new Dictionary<string, string> { ["@id"] = BaseAddress + "v3/flat/", ["@type"] = "PackageBaseAddress/3.0.0" },
                    // As nuget.org's index: NuGet clients look for different spellings of the search type.
                    new Dictionary<string, string> { ["@id"] = BaseAddress + "v3/search", ["@type"] = "SearchQueryService" },
                    new Dictionary<string, string> { ["@id"] = BaseAddress + "v3/search", ["@type"] = "SearchQueryService/3.0.0-beta" },
                    new Dictionary<string, string> { ["@id"] = BaseAddress + "v3/search", ["@type"] = "SearchQueryService/3.0.0-rc" },
                    new Dictionary<string, string> { ["@id"] = BaseAddress + "v3/search", ["@type"] = "SearchQueryService/3.5.0" },
                },
            },
            ["v3", "search"] => new
            {
                totalHits = _packages.Count,
                data = _packages.Select(package => new Dictionary<string, object>
                {
                    ["id"] = package.Key,
                    ["version"] = package.Value.Keys.Last(),
                    ["title"] = "Ocean",
                    ["description"] = $"Theme {package.Key}.",
                    ["tags"] = _tags[package.Key].Split(' '),
                    ["versions"] = package.Value.Keys.Select(Version).ToArray(),
                }).ToArray(),
            },
            ["v3", "flat", var id, "index.json"] when _packages.TryGetValue(id, out var versions) => new { versions = versions.Keys.ToArray() },
            _ => null,
        };
        if (json is not null) return (200, JsonSerializer.SerializeToUtf8Bytes(json), "application/json");
        if (segments is ["v3", "flat", var packageId, var version, _] && _packages.TryGetValue(packageId, out var all)
            && all.TryGetValue(version, out var bytes))
            return (200, bytes, "application/octet-stream");
        return (404, [], "text/plain");
    }

    private Dictionary<string, object> Version(string version) => new() { ["version"] = version, ["downloads"] = 0, ["@id"] = BaseAddress };

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _listener.Close();
    }
}
