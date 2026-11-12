namespace Invoklet.Plugins.Hosting;

/// <summary>
/// Finds the manifests under the plugin sources, in an order that is the same on every start and every machine: sources in the order
/// given (the application's own folder before the user's), and within one, by plugin id then relative path, ordinal. A plugin is a
/// folder with <c>invoklet-plugin.json</c> at most two levels under the source (<c>plugins/&lt;id&gt;</c> or
/// <c>plugins/&lt;role&gt;/&lt;id&gt;</c> or <c>plugins/&lt;id&gt;/&lt;version&gt;</c>). An explicit development source can also be a plugin folder itself.
/// Nothing under a plugin's folder is scanned further. Reading only; nothing is loaded.
/// </summary>
public static class PluginDiscovery
{
    private const int MaxDepth = 2;

    public static IReadOnlyList<DiscoveredPlugin> Scan(IEnumerable<PluginSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var found = new List<DiscoveredPlugin>();
        foreach (var source in sources)
        {
            var plugins = new List<DiscoveredPlugin>();
            Collect(source.Directory, 0, source, plugins);
            found.AddRange(plugins.OrderBy(p => p.Id, StringComparer.Ordinal)
                .ThenBy(p => Path.GetRelativePath(source.Directory, p.Directory), StringComparer.Ordinal));
        }
        return found;
    }

    /// <summary>One plugin folder, read the same way a scan reads it.</summary>
    public static DiscoveredPlugin ReadFolder(string directory, PluginSource source)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(source);
        return Read(Path.Combine(directory, PluginContracts.ManifestFileName), source, ignoreMissing: false)!;
    }

    private static void Collect(string directory, int depth, PluginSource source, List<DiscoveredPlugin> found)
    {
        var manifest = Path.Combine(directory, PluginContracts.ManifestFileName);
        if ((depth > 0 || source.Trust == PluginTrust.Development) && Read(manifest, source, ignoreMissing: true) is { } plugin)
        {
            found.Add(plugin);
            return;
        }
        if (depth >= MaxDepth) return;
        string[] children;
        try { children = Directory.GetDirectories(directory); }
        catch (DirectoryNotFoundException) when (depth == 0) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            found.Add(new DiscoveredPlugin(folderName, null, directory, source,
                [PluginDiagnostic.Error(folderName, PluginDiagnosticCode.DirectoryUnreadable,
                    $"The plugin directory '{folderName}' could not be scanned.", ex.Message)]));
            return;
        }
        foreach (var child in children) Collect(child, depth + 1, source, found);
    }

    private static DiscoveredPlugin? Read(string manifestPath, PluginSource source, bool ignoreMissing)
    {
        var directory = Path.GetDirectoryName(manifestPath)!;
        var folderName = Path.GetFileName(directory);
        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ignoreMissing && ex is (FileNotFoundException or DirectoryNotFoundException))
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var unreadable = PluginDiagnostic.Error(folderName, PluginDiagnosticCode.ManifestUnreadable,
                $"The manifest of '{folderName}' could not be read.", ex.Message);
            return new DiscoveredPlugin(folderName, null, directory, source, [unreadable]);
        }

        var result = PluginManifestReader.Read(json, folderName);
        return new DiscoveredPlugin(result.Manifest?.Id ?? folderName, result.Manifest, directory, source, result.Diagnostics);
    }
}
