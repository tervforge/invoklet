using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Invoklet.Plugins.Hosting;

/// <summary>
/// Turns the manifests found into one <see cref="PluginSession"/>: checks each (its roles, contract versions, Invoklet version and
/// dependencies) before touching its code, loads the accepted ones in dependency order, each in its own context, and registers their
/// roles through the handlers. One plugin's failure is that plugin's diagnostic and its dependants'; it never stops the start or
/// touches another plugin. Nothing here is concurrent: it runs once, at start-up, before the application needs any plugin.
/// </summary>
public sealed class PluginLoader
{
    private readonly PluginLoaderOptions _options;
    private readonly IReadOnlyDictionary<PluginRole, IPluginRoleHandler> _handlers;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public PluginLoader(PluginLoaderOptions options, IReadOnlyList<IPluginRoleHandler> handlers, ILoggerFactory loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(handlers);
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<PluginLoader>();
        var byRole = new Dictionary<PluginRole, IPluginRoleHandler>();
        foreach (var handler in handlers)
        {
            if (!byRole.TryAdd(handler.Role, handler))
                throw new ArgumentException($"Two handlers for the role {handler.Role}.", nameof(handlers));
        }
        _handlers = byRole;
    }

    public PluginSession Load(IReadOnlyList<DiscoveredPlugin> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        var candidates = discovered.Select(d => new Candidate(d)).ToList();

        RejectUnreadable(candidates);
        RejectDuplicates(candidates);
        foreach (var candidate in candidates.Where(c => !c.Rejected)) CheckRolesAndVersion(candidate);
        var order = OrderByDependencies(candidates);
        var shared = new HashSet<string>(_options.SharedAssemblies, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in order) LoadOne(candidate, shared);

        var loaded = order.Where(c => !c.Rejected)
            .Select(c => new LoadedPlugin(c.Manifest!, c.Directory, c.Trust, c.Diagnostics)).ToList();
        var rejected = candidates.Where(c => c.Rejected)
            .Select(c => new RejectedPlugin(c.Id, c.Manifest, c.Directory, c.Trust, c.Diagnostics)).ToList();
        foreach (var plugin in loaded) _logger.LogInformation("Loaded plugin {PluginId} {Version} ({Trust})", plugin.Id, plugin.Manifest.Version, plugin.Trust);
        foreach (var plugin in rejected)
            _logger.LogWarning("Rejected plugin {PluginId}: {Reasons}", plugin.Id,
                string.Join("; ", plugin.Diagnostics.Where(d => d.IsError).Select(d => $"{d.Code}: {d.Message}{(d.Detail is null ? "" : $" [{d.Detail}]")}")));
        return new PluginSession(loaded, rejected);
    }

    private static void RejectUnreadable(List<Candidate> candidates)
    {
        foreach (var candidate in candidates)
            if (candidate.Manifest is null) candidate.Rejected = true;
    }

    // The first of an id wins: sources come in trust order (bundled, then the user's), and a user's copy of a bundled id must not
    // replace what shipped with the application.
    private static void RejectDuplicates(List<Candidate> candidates)
    {
        var seen = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(c => !c.Rejected))
        {
            if (seen.TryAdd(candidate.Id, candidate)) continue;
            candidate.Reject(PluginDiagnosticCode.DuplicateId,
                $"'{candidate.Id}' is already provided by another plugin ({seen[candidate.Id].Trust}); this copy was skipped.",
                $"skipped: {candidate.Directory}; kept: {seen[candidate.Id].Directory}");
        }
    }

    private void CheckRolesAndVersion(Candidate candidate)
    {
        var manifest = candidate.Manifest!;
        if (!manifest.RequiresCode && manifest.Entry is not null)
        {
            candidate.Reject(PluginDiagnosticCode.ManifestInvalid, $"'{candidate.Id}' declares code for a data-only plugin.");
            return;
        }
        foreach (var (role, version) in manifest.Roles)
        {
            if (!PluginRoles.IsRegistered(role))
            {
                if (!PluginContracts.SupportedVersions(role).Contains(version))
                    candidate.Reject(PluginDiagnosticCode.UnsupportedContractVersion,
                        $"'{candidate.Id}' is written for version {version} of the {PluginRoles.Name(role)} contract; this Invoklet supports {Versions(PluginContracts.SupportedVersions(role))}.");
                continue;
            }
            if (!_handlers.TryGetValue(role, out var handler))
            {
                candidate.Reject(PluginDiagnosticCode.UnsupportedRole,
                    $"'{candidate.Id}' takes the role '{PluginRoles.Name(role)}', which this Invoklet can't host.");
                continue;
            }
            if (handler.ContractVersion != version)
                candidate.Reject(PluginDiagnosticCode.UnsupportedContractVersion,
                    $"'{candidate.Id}' is written for version {version} of the {PluginRoles.Name(role)} contract; this Invoklet supports {handler.ContractVersion}.");
        }

        if (manifest.MinAppVersion is { } minimum && minimum > _options.ApplicationVersion)
            candidate.Reject(PluginDiagnosticCode.ApplicationTooOld,
                $"'{candidate.Id}' needs Invoklet {minimum} or newer; this is {_options.ApplicationVersion}.");
    }

    private static string Versions(IReadOnlyList<int> versions) => string.Join(", ", versions);

    // Dependencies first, in the order the manifests were found otherwise. A missing or rejected dependency rejects the dependant; a
    // cycle rejects everything on it.
    private static List<Candidate> OrderByDependencies(List<Candidate> candidates)
    {
        var byId = candidates.Where(c => !c.Rejected).ToDictionary(c => c.Id, StringComparer.Ordinal);
        var order = new List<Candidate>();
        var visiting = new HashSet<Candidate>();
        var done = new HashSet<Candidate>();

        foreach (var candidate in candidates) Visit(candidate);
        return order;

        void Visit(Candidate candidate)
        {
            if (done.Contains(candidate)) return;
            if (!visiting.Add(candidate))
            {
                candidate.Reject(PluginDiagnosticCode.DependencyCycle, $"'{candidate.Id}' is in a cycle of dependencies.");
                return;
            }

            if (!candidate.Rejected)
            {
                foreach (var dependencyId in candidate.Manifest!.DependsOn)
                {
                    if (!byId.TryGetValue(dependencyId, out var dependency))
                    {
                        candidate.Reject(PluginDiagnosticCode.DependencyMissing,
                            $"'{candidate.Id}' depends on '{dependencyId}', which is not installed or didn't load.");
                        continue;
                    }
                    Visit(dependency);
                    if (!dependency.Rejected) candidate.Dependencies.Add(dependency);
                    // One reason is enough: a member of a cycle was rejected as such when the walk came back to it.
                    else if (!candidate.Rejected)
                        candidate.Reject(PluginDiagnosticCode.DependencyRejected,
                            $"'{candidate.Id}' depends on '{dependencyId}', which didn't load.");
                }
            }

            visiting.Remove(candidate);
            done.Add(candidate);
            order.Add(candidate);
        }
    }

    private void LoadOne(Candidate candidate, IReadOnlySet<string> shared)
    {
        if (candidate.Rejected) return;
        foreach (var dependency in candidate.Dependencies)
        {
            if (!dependency.Rejected) continue;
            candidate.Reject(PluginDiagnosticCode.DependencyRejected, $"'{candidate.Id}' depends on '{dependency.Id}', which didn't load.");
            return;
        }

        var manifest = candidate.Manifest!;
        var context = new PluginContext(manifest, candidate.Directory, candidate.Trust, _loggerFactory.CreateLogger("Invoklet.Plugins." + candidate.Id));
        IInvokletPlugin? instance = null;
        if (manifest.Entry is { } entry)
        {
            instance = Instantiate(candidate, entry, shared);
            if (instance is null) return;
            try
            {
                instance.Initialize(context);
            }
            catch (Exception ex)
            {
                candidate.Reject(PluginDiagnosticCode.RegistrationFailed, $"'{candidate.Id}' failed to initialize.", ex.ToString());
                return;
            }
        }

        var prepared = new List<(PluginRole Role, PluginRoleRegistration Registration)>();
        foreach (var role in PluginRoles.RegistrationOrder)
        {
            if (!manifest.HasRole(role) || !PluginRoles.IsRegistered(role)) continue;
            try
            {
                var rolePlugin = PluginRoles.RequiresCode(role) ? instance : null;
                prepared.Add((role, _handlers[role].Prepare(new PluginRegistration(context, rolePlugin))));
            }
            catch (Exception ex)
            {
                candidate.Reject(PluginDiagnosticCode.RegistrationFailed,
                    $"'{candidate.Id}' failed to register as a {PluginRoles.Name(role)}.", ex.ToString());
                return;
            }
        }

        for (var index = 0; index < prepared.Count; index++)
        {
            var (role, registration) = prepared[index];
            try
            {
                registration.Apply();
            }
            catch (Exception ex)
            {
                for (var applied = index; applied >= 0; applied--)
                {
                    try { prepared[applied].Registration.Rollback(); }
                    catch (Exception rollbackError)
                    {
                        _logger.LogError(rollbackError, "Could not roll back {Role} registration for {PluginId}", prepared[applied].Role, candidate.Id);
                    }
                }
                candidate.Reject(PluginDiagnosticCode.RegistrationFailed,
                    $"'{candidate.Id}' failed to register as a {PluginRoles.Name(role)}.", ex.ToString());
                return;
            }
        }
    }

    private IInvokletPlugin? Instantiate(Candidate candidate, PluginEntryPoint entry, IReadOnlySet<string> shared)
    {
        var assemblyPath = Path.Combine(candidate.Directory, entry.Assembly);
        if (!File.Exists(assemblyPath))
        {
            candidate.Reject(PluginDiagnosticCode.EntryAssemblyMissing, $"'{candidate.Id}' has no entry assembly beside its manifest.", assemblyPath);
            return null;
        }

        Assembly assembly;
        try
        {
            candidate.LoadContext = new PluginLoadContext(candidate.Id, assemblyPath, shared,
                [.. candidate.Dependencies.Select(d => d.LoadContext).OfType<PluginLoadContext>()]);
            assembly = candidate.LoadContext.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            candidate.Reject(PluginDiagnosticCode.EntryAssemblyUnloadable, $"The entry assembly for '{candidate.Id}' could not be loaded.", ex.ToString());
            return null;
        }

        Type? type;
        try
        {
            type = assembly.GetType(entry.Type, throwOnError: false);
        }
        catch (Exception ex)
        {
            candidate.Reject(PluginDiagnosticCode.EntryTypeMissing, $"The entry type for '{candidate.Id}' could not be loaded.", ex.ToString());
            return null;
        }
        if (type is null)
        {
            candidate.Reject(PluginDiagnosticCode.EntryTypeMissing, $"The entry type for '{candidate.Id}' was not found.", $"Entry type: {entry.Type}");
            return null;
        }
        if (!typeof(IInvokletPlugin).IsAssignableFrom(type))
        {
            var hint = type.GetInterfaces().Any(i => i.FullName == typeof(IInvokletPlugin).FullName)
                ? "it implements IInvokletPlugin from its own copy of Invoklet.Plugins; the plugin must not ship that assembly"
                : "it does not implement IInvokletPlugin";
            candidate.Reject(PluginDiagnosticCode.EntryTypeInvalid, $"'{candidate.Id}' has an invalid entry point ({hint}).", $"Entry type: {entry.Type}");
            return null;
        }
        if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
        {
            candidate.Reject(PluginDiagnosticCode.EntryTypeInvalid, $"The entry type for '{candidate.Id}' needs a public parameterless constructor.",
                $"Entry type: {entry.Type}");
            return null;
        }

        IInvokletPlugin instance;
        try
        {
            instance = (IInvokletPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            candidate.Reject(PluginDiagnosticCode.EntryTypeInvalid, $"The entry point for '{candidate.Id}' could not be created.", ex.ToString());
            return null;
        }

        PluginDescriptor? descriptor;
        try
        {
            descriptor = instance.Descriptor;
        }
        catch (Exception ex)
        {
            candidate.Reject(PluginDiagnosticCode.DescriptorMismatch, $"'{candidate.Id}' failed to describe itself.", ex.ToString());
            return null;
        }
        if (descriptor is null || descriptor.Id != candidate.Id || descriptor.Version != candidate.Manifest!.Version)
        {
            candidate.Reject(PluginDiagnosticCode.DescriptorMismatch,
                $"The entry point for '{candidate.Id}' does not match its manifest.",
                $"Declared: {candidate.Id} {candidate.Manifest!.Version}; entry point: {descriptor?.Id} {descriptor?.Version}");
            return null;
        }
        return instance;
    }

    private sealed class Candidate(DiscoveredPlugin discovered)
    {
        private readonly List<PluginDiagnostic> _diagnostics = [.. discovered.Diagnostics];

        public string Id => discovered.Id;
        public PluginManifest? Manifest => discovered.Manifest;
        public string Directory => discovered.Directory;
        public PluginTrust Trust => discovered.Source.Trust;
        public IReadOnlyList<PluginDiagnostic> Diagnostics => _diagnostics;
        public bool Rejected { get; set; }
        public List<Candidate> Dependencies { get; } = [];
        public PluginLoadContext? LoadContext { get; set; }

        public void Reject(PluginDiagnosticCode code, string message, string? detail = null)
        {
            _diagnostics.Add(PluginDiagnostic.Error(Id, code, message, detail));
            Rejected = true;
        }
    }
}
