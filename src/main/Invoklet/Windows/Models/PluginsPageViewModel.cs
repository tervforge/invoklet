using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Shared;
using ReactiveUI;

namespace Invoklet.Windows.Models;

public sealed record PluginStatusRow(string Name, string Id, string? Version, string Roles, string Trust, string Status,
    IReadOnlyList<string> Diagnostics)
{
    public string Metadata => string.Join(" · ", new[] { Version, Roles, Trust }.Where(value => !string.IsNullOrEmpty(value)));
    public bool IsContentVerified { get; init; }
    public bool CanToggle { get; init; }
    public bool CanRemove { get; init; }
    public bool DesiredEnabled { get; init; }
    public string ToggleLabel => DesiredEnabled ? "Disable" : "Enable";
    public string ToggleAutomationName => $"{ToggleLabel} {Name}";
}

/// <param name="startupError">What kept the host from applying its plugin arguments at this start, if anything; shown first.</param>
public sealed class PluginsPageViewModel(PluginSession session, Func<PluginInventory> readInventory, Func<Task> restart,
    string? startupError = null, Func<string, bool>? directoryExists = null, Action<string, bool>? setEnabled = null,
    Func<PluginPackageInstaller>? createInstaller = null) : ViewModelBase
{
    private IReadOnlyList<PluginStatusRow> _rows = [];
    private IReadOnlyList<string> _diagnostics = [];
    private bool _hasPendingRestart;
    private string? _writeError;

    public IReadOnlyList<PluginStatusRow> Rows => _rows;
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public bool HasPendingRestart => _hasPendingRestart;
    public bool IsEmpty => Rows.Count == 0 && Diagnostics.Count == 0;
    public bool CanInstall => createInstaller is not null;

    internal PluginPackageInstaller CreateInstaller() => createInstaller!();

    internal void ReportPackageError()
    {
        _writeError = "Could not update the plugin package. Check its manifest, ID and version, then try again.";
        Refresh();
    }

    internal void PackageChanged()
    {
        _writeError = null;
        Refresh();
    }

    public void Toggle(PluginStatusRow row)
    {
        if (setEnabled is null || !Rows.Contains(row) || !row.CanToggle) return;
        try
        {
            setEnabled(row.Id, !row.DesiredEnabled);
            _writeError = null;
        }
        catch (PluginRegistrationException ex)
        {
            _writeError = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _writeError = "Could not change the plugin state. Refresh and try again.";
        }

        Refresh();
    }

    public void Refresh()
    {
        var rows = session.Loaded.Select(plugin => CurrentRow(plugin.Id, plugin.Manifest, plugin.Trust, "Loaded", plugin.Diagnostics))
            .Concat(session.Rejected.Select(plugin => CurrentRow(plugin.Id, plugin.Manifest, plugin.Trust,
                plugin.Diagnostics.Any(item => item.Code == PluginDiagnosticCode.PluginDisabled) ? "Disabled" : "Rejected", plugin.Diagnostics)))
            .ToList();
        var rowDiagnostics = session.Loaded.SelectMany(plugin => plugin.Diagnostics)
            .Concat(session.Rejected.SelectMany(plugin => plugin.Diagnostics)).ToHashSet();
        var diagnostics = session.Diagnostics.Where(diagnostic => !rowDiagnostics.Contains(diagnostic))
            .Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal).ToList();
        if (!string.IsNullOrWhiteSpace(startupError)) diagnostics.Insert(0, startupError);
        if (_writeError is not null) diagnostics.Insert(0, _writeError);

        var inventory = readInventory();
        IReadOnlyList<string> pending;
        try { pending = session.GetPendingRestart(inventory); }
        catch (InvalidOperationException)
        {
            pending = [];
            const string unreadable = "The user plugin inventory could not be read.";
            if (!diagnostics.Contains(unreadable, StringComparer.Ordinal)) diagnostics.Add(unreadable);
        }

        var entries = inventory.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var unreadableInventory = diagnostics.Contains("The user plugin inventory could not be read.", StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var entry = entries.GetValueOrDefault(row.Id);
            var bundled = row.Trust == nameof(PluginTrust.Bundled);
            var installed = row.Trust == nameof(PluginTrust.Trusted) && entry is { Approved: true }
                && entry.Source != PluginInventoryStore.BundledSource && entry.Version == row.Version;
            var manageable = installed || bundled && (row.Status is "Loaded" or "Disabled");
            rows[index] = row with
            {
                CanToggle = setEnabled is not null && !unreadableInventory && manageable,
                CanRemove = CanInstall && !unreadableInventory && installed
                    && PluginPackageInstaller.IsManaged(entry),
                DesiredEnabled = bundled ? entry is not { Source: PluginInventoryStore.BundledSource, Enabled: false } : entry?.Enabled == true,
            };
        }
        var missing = directoryExists is null ? [] : session.Loaded
            .Where(plugin => plugin.Trust == PluginTrust.Trusted && entries.TryGetValue(plugin.Id, out var entry)
                && entry.Version == plugin.Manifest.Version && !directoryExists(plugin.Directory))
            .Select(plugin => plugin.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in pending.Union(missing, StringComparer.Ordinal))
        {
            var current = rows.Find(row => row.Id == id && row.Trust == nameof(PluginTrust.Bundled))
                ?? rows.Find(row => row.Id == id && row.Trust == nameof(PluginTrust.Trusted));
            var desired = entries.GetValueOrDefault(id);
            rows.Add(new PluginStatusRow(current?.Name ?? id, id, desired?.Version ?? current?.Version,
                current?.Roles ?? (desired is null ? "" : string.Join(", ", desired.Roles)), current?.Trust ?? nameof(PluginTrust.Trusted), "Pending restart",
                current?.Trust == nameof(PluginTrust.Bundled) ? [desired is null ? "Will be enabled." : "Will be disabled."]
                    : desired is null ? ["Removed from the plugin inventory."] : missing.Contains(id) ? ["The plugin folder is missing."]
                    : [desired.Enabled ? "Will be enabled." : "Will be disabled."])
            {
                CanToggle = setEnabled is not null && !unreadableInventory && current is null && desired is { Approved: true },
                DesiredEnabled = desired?.Enabled == true,
                CanRemove = CanInstall && !unreadableInventory && current is null
                    && PluginPackageInstaller.IsManaged(desired),
            });
        }

        this.RaiseAndSetIfChanged(ref _rows, rows.AsReadOnly(), nameof(Rows));
        this.RaiseAndSetIfChanged(ref _diagnostics, diagnostics.AsReadOnly(), nameof(Diagnostics));
        this.RaiseAndSetIfChanged(ref _hasPendingRestart, pending.Count > 0 || missing.Count > 0, nameof(HasPendingRestart));
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    public Task RestartAsync() => HasPendingRestart ? restart() : Task.CompletedTask;

    private static PluginStatusRow CurrentRow(string id, PluginManifest? manifest, PluginTrust trust, string status,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(manifest?.DisplayName ?? id, id, manifest?.Version,
            manifest is null ? "" : string.Join(", ", manifest.Roles.Keys.Select(PluginRoles.Name).Order(StringComparer.Ordinal)),
            trust.ToString(), status, diagnostics.Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal).ToArray())
        { IsContentVerified = status == "Loaded" && trust is PluginTrust.Bundled or PluginTrust.Trusted };
}
