using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Invoklet.Shared;
using ReactiveUI;

namespace Invoklet.Windows.Models;

public sealed record PluginStatusRow(string Name, string Id, string? Version, string Roles, string Trust, string Status,
    IReadOnlyList<string> Diagnostics)
{
    public string Metadata => string.Join(" · ", new[] { Version, Roles, Trust }.Where(value => !string.IsNullOrEmpty(value)));
}

public sealed class PluginsPageViewModel(PluginSession session, Func<PluginInventory> readInventory, Func<Task> restart) : ViewModelBase
{
    private IReadOnlyList<PluginStatusRow> _rows = [];
    private IReadOnlyList<string> _diagnostics = [];
    private bool _hasPendingRestart;

    public IReadOnlyList<PluginStatusRow> Rows => _rows;
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public bool HasPendingRestart => _hasPendingRestart;
    public bool IsEmpty => Rows.Count == 0 && Diagnostics.Count == 0;

    public void Refresh()
    {
        var rows = session.Loaded.Select(plugin => CurrentRow(plugin.Id, plugin.Manifest, plugin.Trust, "Loaded", plugin.Diagnostics))
            .Concat(session.Rejected.Select(plugin => CurrentRow(plugin.Id, plugin.Manifest, plugin.Trust, "Rejected", plugin.Diagnostics)))
            .ToList();
        var rowDiagnostics = session.Loaded.SelectMany(plugin => plugin.Diagnostics)
            .Concat(session.Rejected.SelectMany(plugin => plugin.Diagnostics)).ToHashSet();
        var diagnostics = session.Diagnostics.Where(diagnostic => !rowDiagnostics.Contains(diagnostic))
            .Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal).ToList();

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
        foreach (var id in pending)
        {
            var current = rows.Find(row => row.Id == id && row.Trust == nameof(PluginTrust.Trusted));
            var desired = entries.GetValueOrDefault(id);
            rows.Add(new PluginStatusRow(current?.Name ?? id, id, desired?.Version ?? current?.Version,
                desired is null ? current?.Roles ?? "" : string.Join(", ", desired.Roles), nameof(PluginTrust.Trusted), "Pending restart",
                desired is null ? ["Removed from the plugin inventory."] : []));
        }

        this.RaiseAndSetIfChanged(ref _rows, rows.AsReadOnly(), nameof(Rows));
        this.RaiseAndSetIfChanged(ref _diagnostics, diagnostics.AsReadOnly(), nameof(Diagnostics));
        this.RaiseAndSetIfChanged(ref _hasPendingRestart, pending.Count > 0, nameof(HasPendingRestart));
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    public Task RestartAsync() => HasPendingRestart ? restart() : Task.CompletedTask;

    private static PluginStatusRow CurrentRow(string id, PluginManifest? manifest, PluginTrust trust, string status,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(manifest?.DisplayName ?? id, id, manifest?.Version,
            manifest is null ? "" : string.Join(", ", manifest.Roles.Keys.Select(PluginRoles.Name).Order(StringComparer.Ordinal)),
            trust.ToString(), status, diagnostics.Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal).ToArray());
}
