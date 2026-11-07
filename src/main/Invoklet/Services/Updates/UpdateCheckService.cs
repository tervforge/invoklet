using Invoklet.Services.Preferences;
using Invoklet.Services.Setup;

namespace Invoklet.Services.Updates;

public sealed class UpdateCheckService(IPreferencesService preferences, IReleaseMetadataReader metadataReader) : IUpdateCheckService
{
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/tervforge/invoklet/releases/latest");
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public Task<UpdateCheckResult> CheckDailyAsync(Version currentVersion, DateTimeOffset now, CancellationToken ct)
    {
        if (preferences.Session.LastUpdateCheckUtc is { } last && now - last < Interval)
            return Task.FromResult(new UpdateCheckResult(false, null));

        preferences.Session.LastUpdateCheckUtc = now;
        try { preferences.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return CheckNowAsync(currentVersion, ct);
    }

    public async Task<UpdateCheckResult> CheckNowAsync(Version currentVersion, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        string? tag;
        try { tag = await metadataReader.ReadStringAsync(LatestReleaseApi, "tag_name", timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, null); }

        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return new(false, null);
        var available = latest > currentVersion
            ? new UpdateAvailable(latest, new Uri($"https://github.com/tervforge/invoklet/releases/tag/{Uri.EscapeDataString(tag)}"))
            : null;
        return new(true, available);
    }
}
