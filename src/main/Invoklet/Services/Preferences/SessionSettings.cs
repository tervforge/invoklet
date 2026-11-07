namespace Invoklet.Services.Preferences;

public sealed class SessionSettings
{
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public List<string> OpenTabs { get; set; } = [];
    public string? ActiveTabPath { get; set; }
}
