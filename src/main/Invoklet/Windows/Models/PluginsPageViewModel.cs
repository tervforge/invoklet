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

/// <summary>A package the plugin feed offers, with what installing it would mean here.</summary>
public sealed record PluginFeedRow(PluginFeedPackage Package, string State, bool CanInstall)
{
    public string Name => Package.Title;
    public string Metadata => $"{Package.Id} · {Package.LatestVersion}";
    public string InstallAutomationName => $"Install {Package.Title}";
}

/// <param name="startupError">What kept the host from applying its plugin arguments at this start, if anything; shown first.</param>
public sealed class PluginsPageViewModel(PluginSession session, Func<PluginInventory> readInventory, Func<Task> restart,
    string? startupError = null, Func<string, bool>? directoryExists = null, Action<string, bool>? setEnabled = null,
    Func<PluginPackageInstaller>? createInstaller = null, Func<PluginFeedResolver>? createFeed = null) : ViewModelBase
{
    private IReadOnlyList<PluginFeedRow> _feedPackages = [];
    private string? _feedStatus;
    private bool _isFeedBusy;

    private IReadOnlyList<PluginStatusRow> _rows = [];
    private IReadOnlyList<string> _diagnostics = [];
    private bool _hasPendingRestart;
    private string? _writeError;

    public IReadOnlyList<PluginStatusRow> Rows => _rows;
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public bool HasPendingRestart => _hasPendingRestart;
    public bool IsEmpty => Rows.Count == 0 && Diagnostics.Count == 0;
    public bool CanInstall => createInstaller is not null;
    public bool CanBrowse => createFeed is not null && createInstaller is not null;
    public IReadOnlyList<PluginFeedRow> FeedPackages => _feedPackages;
    public string? FeedStatus => _feedStatus;
    public bool IsFeedBusy => _isFeedBusy;

    /// <summary>Lists the feed's plugin packages; nothing is downloaded until one is installed.</summary>
    internal async Task BrowseAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBrowse || IsFeedBusy) return;
        SetFeed(busy: true, "Loading the plugin feed…");
        try
        {
            var packages = await createFeed!().ListAsync(cancellationToken);
            this.RaiseAndSetIfChanged(ref _feedPackages, packages.Select(FeedRow).ToArray(), nameof(FeedPackages));
            SetFeed(busy: false, packages.Count == 0 ? "The plugin feed lists no plugins." : null);
        }
        catch (PluginFeedException ex)
        {
            SetFeed(busy: false, ex.Message);
        }
    }

    /// <summary>
    /// Downloads compatible packages and asks for approval of every new dependency before installing any of them.
    /// </summary>
    internal async Task InstallFromFeedAsync(PluginFeedRow row, Func<PluginManifest, string, Task<bool>> confirm,
        CancellationToken cancellationToken = default)
    {
        if (!CanBrowse || IsFeedBusy || !row.CanInstall) return;
        SetFeed(busy: true, $"Finding a version of {row.Name} for this Invoklet…");
        string? staging = null;
        var prepared = new List<PreparedPluginPackage>();
        string? status = null;
        try
        {
            PluginPackageInstaller? installer = null;
            var feed = createFeed!();
            var inventory = readInventory();
            var pending = session.GetPendingRestart(inventory);

            staging = Directory.CreateTempSubdirectory("invoklet-plugin-").FullName;
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var ready = new HashSet<string>(StringComparer.Ordinal);
            var installOrder = new List<(PreparedPluginPackage Package, string Path)>();
            status = await PrepareAsync(row.Package.Id, row.Name);
            if (status is not null) return;

            foreach (var (package, path) in installOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await confirm(package.Manifest, path)) return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                foreach (var (package, _) in installOrder) installer!.Install(package);
            });
            PackageChanged(null);

            async Task<string?> PrepareAsync(string id, string name)
            {
                if (ready.Contains(id)) return null;
                if (!visiting.Add(id)) return $"Plugin '{id}' has a circular dependency.";
                if (prepared.Count >= 16) return "This installation requires more than 16 plugin packages.";
                var skipped = new List<string>();
                if (await feed.ChooseAsync(id, skipped, cancellationToken) is not { } choice)
                    return $"No version of {name} works with this Invoklet. Newest: {skipped[0]}";

                var path = Path.Combine(staging, $"{choice.Id}.{choice.Version}.nupkg");
                await File.WriteAllBytesAsync(path, choice.Package, cancellationToken);
                installer ??= createInstaller!();
                var package = await Task.Run(() => installer.PrepareArchive(path));
                prepared.Add(package);
                foreach (var dependency in package.Manifest.DependsOn)
                {
                    if (DependencyAvailable(inventory, pending, dependency)) continue;
                    if (inventory.Entries.Any(entry => entry.Id == dependency) || session.Rejected.Any(plugin => plugin.Id == dependency))
                        return $"Enable or repair required plugin '{dependency}' in Preferences → Plugins first.";
                    // The feed may publish the plugin under a prefixed package id; the listing says which.
                    var dependencyPackage = PluginFeedResolver.Find(_feedPackages.Select(item => item.Package), dependency);
                    if (await PrepareAsync(dependencyPackage?.Id ?? dependency, dependencyPackage?.Title ?? dependency) is { } error) return error;
                }
                installOrder.Add((package, path));
                visiting.Remove(id);
                ready.Add(id);
                return null;
            }
        }
        catch (PluginFeedException ex)
        {
            status = ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            status = PackageError;
            PackageChanged(status);
        }
        finally
        {
            foreach (var package in prepared)
            {
                try { package.Dispose(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            SetFeed(busy: false, status);
            this.RaiseAndSetIfChanged(ref _feedPackages, _feedPackages.Select(item => FeedRow(item.Package)).ToArray(), nameof(FeedPackages));
        }
    }

    private bool DependencyAvailable(PluginInventory inventory, IReadOnlyList<string> pending, string id) =>
        inventory.Entries.FirstOrDefault(entry => entry.Id == id) is { } dependency
            ? dependency.Enabled && dependency.Approved
                && (!session.Rejected.Any(plugin => plugin.Id == id) || pending.Contains(id))
            : session.Find(id) is { Trust: PluginTrust.Bundled or PluginTrust.Development };

    private PluginFeedRow FeedRow(PluginFeedPackage package)
    {
        if (Rows.Any(row => PluginFeedResolver.Names(package.Id, row.Id) && row.Trust == nameof(PluginTrust.Bundled)))
            return new PluginFeedRow(package, "Included with Invoklet", CanInstall: false);
        PluginInventoryEntry? entry;
        try { entry = readInventory().Entries.Where(item => PluginFeedResolver.Names(package.Id, item.Id)).MaxBy(item => item.Id.Length); }
        catch (InvalidOperationException) { entry = null; }
        if (entry is { Source: not PluginInventoryStore.BundledSource })
            return new PluginFeedRow(package, entry.Version == package.LatestVersion ? $"Installed {entry.Version}"
                : $"Installed {entry.Version}; {package.LatestVersion} is available. To update, remove it and install again.", CanInstall: false);
        return new PluginFeedRow(package, "", CanInstall: CanInstall);
    }

    private void SetFeed(bool busy, string? status)
    {
        this.RaiseAndSetIfChanged(ref _isFeedBusy, busy, nameof(IsFeedBusy));
        this.RaiseAndSetIfChanged(ref _feedStatus, status, nameof(FeedStatus));
    }

    /// <summary>Reviews a NuGet plugin package, asks <paramref name="confirm"/> to approve it and installs it for the next start.</summary>
    internal async Task InstallAsync(string path, Func<PluginManifest, Task<bool>> confirm)
    {
        if (createInstaller is null) return;
        try
        {
            var installer = createInstaller();
            using var package = await Task.Run(() => installer.PrepareArchive(path));
            var inventory = readInventory();
            var pending = session.GetPendingRestart(inventory);
            var unavailableDependencies = package.Manifest.DependsOn.Where(id => !DependencyAvailable(inventory, pending, id)).ToArray();
            if (unavailableDependencies.Length > 0)
            {
                var message = $"Install or enable these required plugins in Preferences → Plugins first: {string.Join(", ", unavailableDependencies)}.";
                PackageChanged(message);
                SetFeed(busy: false, message);
                return;
            }
            if (!await confirm(package.Manifest)) return;
            await Task.Run(() => installer.Install(package));
            PackageChanged(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            PackageChanged(PackageError);
        }
    }

    internal async Task RemoveAsync(PluginStatusRow row, Func<Task<bool>> confirm)
    {
        if (createInstaller is null || !row.CanRemove || !Rows.Contains(row)) return;
        try
        {
            if (!await confirm()) return;
            var installer = createInstaller();
            await Task.Run(() => installer.Remove(row.Id));
            PackageChanged(null);
        }
        catch (PluginRegistrationException ex)
        {
            PackageChanged(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            PackageChanged(PackageError);
        }
    }

    internal void ReportPackageError() => PackageChanged(PackageError);

    private const string PackageError = "Could not update the plugin package. Check its manifest, ID and version, then try again.";

    private void PackageChanged(string? error)
    {
        _writeError = error;
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
                RejectedStatus(plugin.Diagnostics), plugin.Diagnostics)))
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
            var canRemove = CanInstall && !unreadableInventory && installed && PluginPackageInstaller.IsManaged(entry);
            rows[index] = row with
            {
                CanToggle = setEnabled is not null && !unreadableInventory && manageable,
                CanRemove = canRemove,
                // Approval covers the exact package bytes, so changed contents are approved again by reinstalling the package.
                Diagnostics = canRemove && row.Status == ContentChanged
                    ? [.. row.Diagnostics, "Remove the plugin and install its package again to approve the new contents."]
                    : row.Diagnostics,
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

    private static string RejectedStatus(IReadOnlyList<PluginDiagnostic> diagnostics) =>
        diagnostics.Any(item => item.Code == PluginDiagnosticCode.PluginDisabled) ? "Disabled"
        : diagnostics.Any(item => item.Code == PluginDiagnosticCode.PluginContentChanged) ? ContentChanged
        : "Rejected";

    private const string ContentChanged = "Content changed";

    private static PluginStatusRow CurrentRow(string id, PluginManifest? manifest, PluginTrust trust, string status,
        IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var messages = diagnostics.Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal);
        return new(manifest?.DisplayName ?? id, id, manifest?.Version,
            manifest is null ? "" : string.Join(", ", manifest.Roles.Keys.Select(PluginRoles.Name).Order(StringComparer.Ordinal)),
            trust.ToString(), status, messages.ToArray())
        { IsContentVerified = status == "Loaded" && trust is PluginTrust.Bundled or PluginTrust.Trusted };
    }
}
