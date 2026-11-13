namespace Invoklet.Plugins.Hosting;

internal sealed class PluginAdmission(PluginLoaderOptions options)
{
    internal IEnumerable<PluginSource> Sources()
    {
        if (options.BundledDirectory is { } bundled) yield return new PluginSource(bundled, PluginTrust.Bundled);
        if (options.UserDirectory is { } user) yield return new PluginSource(user, PluginTrust.Trusted);
        if (options.DevelopmentDirectory is { } development) yield return new PluginSource(development, PluginTrust.Development);
    }

    internal PluginDiagnostic? Check(DiscoveredPlugin plugin, out PluginFiles? files, out PluginManifest? manifest)
    {
        files = null;
        manifest = plugin.Manifest!;
        var id = manifest.Id;
        if (plugin.Id != id)
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.ManifestInvalid, $"'{id}' does not match the discovered plugin id.");
        var source = plugin.Source;
        var configured = Sources().FirstOrDefault(candidate => candidate.Trust == source.Trust);
        if (configured is null || !SameDirectory(configured.Directory, source.Directory) || !PluginFiles.IsInside(configured.Directory, plugin.Directory))
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginSourceNotConfigured, $"'{id}' is outside its configured plugin source.", plugin.Directory);

        var bundled = options.BundledCatalog.Find(id);
        if (source.Trust != PluginTrust.Bundled && bundled is not null)
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.DuplicateId, $"'{id}' is reserved for a bundled plugin.");

        if (source.Trust == PluginTrust.Development)
            return PluginDiagnostic.Warning(id, PluginDiagnosticCode.DevelopmentPlugin, $"'{id}' is running in plugin development mode without approval.");

        var user = options.Inventory.Find(id);
        var readError = source.Trust == PluginTrust.Bundled ? options.BundledCatalog.ReadError : options.Inventory.ReadError;
        if (readError is not null)
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.InventoryUnreadable, $"The plugin catalogue for '{id}' could not be read.", readError);

        var listedVersion = source.Trust == PluginTrust.Bundled ? bundled?.Version : user?.Version;
        if (listedVersion != manifest.Version)
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginNotListed, $"'{id}' version {manifest.Version} is not listed in its plugin catalogue.");

        if (source.Trust == PluginTrust.Trusted)
        {
            if (!user!.Enabled)
                return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginDisabled, $"'{id}' is disabled.");
            if (!user.Approved)
                return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginNotApproved, $"'{id}' needs approval before it can load.");
        }

        if (source.Trust == PluginTrust.Bundled && user is { Source: PluginInventoryStore.BundledSource, Enabled: false })
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginDisabled, $"'{id}' is disabled.");

        try
        {
            PluginFiles.RejectLinksBetween(Path.GetFullPath(configured.Directory), plugin.Directory);
            files = PluginFiles.Read(plugin.Directory, options.NativeLibraryCacheDirectory);
            var expectedHash = source.Trust == PluginTrust.Bundled ? bundled!.ContentHash : user!.ContentHash;
            if (!string.Equals(files.ContentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginContentChanged, $"The contents of '{id}' changed since they were approved.");

            using var verified = files.ReadVerified(Path.Combine(plugin.Directory, PluginContracts.ManifestFileName));
            using var reader = new StreamReader(verified);
            var approved = PluginManifestReader.Read(reader.ReadToEnd(), id).Manifest;
            if (approved is null || approved.Id != id || approved.Version != listedVersion
                || (source.Trust == PluginTrust.Trusted && !approved.Roles.Keys.Select(PluginRoles.Name).ToHashSet(StringComparer.Ordinal).SetEquals(user!.Roles)))
                return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginContentChanged, $"The approved manifest for '{id}' does not match its plugin catalogue.");
            manifest = approved;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PluginDiagnostic.Error(id, PluginDiagnosticCode.PluginContentUnreadable, $"The approved files of '{id}' could not be verified.", ex.Message);
        }
    }

    private static bool SameDirectory(string first, string second) =>
        Path.GetRelativePath(Path.GetFullPath(first), Path.GetFullPath(second)) == ".";
}
