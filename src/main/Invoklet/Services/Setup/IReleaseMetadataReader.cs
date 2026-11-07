namespace Invoklet.Services.Setup;

public interface IReleaseMetadataReader
{
    Task<string?> ReadStringAsync(Uri url, string propertyName, CancellationToken ct);
}
