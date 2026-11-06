namespace Invoklet.Services.Updates;

public sealed record UpdateAvailable(Version Version, Uri ReleaseUri);

public sealed record UpdateCheckResult(bool Checked, UpdateAvailable? Available);

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckDailyAsync(Version currentVersion, DateTimeOffset now, CancellationToken ct);
    Task<UpdateCheckResult> CheckNowAsync(Version currentVersion, CancellationToken ct);
}
