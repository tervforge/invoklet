using System.Text.Json;

namespace Invoklet.Services.Setup;

public sealed class ReleaseMetadataReader(ISetupDownloader downloader) : IReleaseMetadataReader
{
    public async Task<string?> ReadStringAsync(Uri url, string propertyName, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await downloader.ReadTextAsync(url, ct));
            return json.RootElement.GetProperty(propertyName).GetString() is { Length: > 0 } value ? value : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
