using System.Reflection;
using System.Runtime.Loader;

namespace Invoklet.Plugins.Hosting;

/// <summary>
/// One plugin's assemblies, apart from every other plugin's: two connectors can bring two versions of the same library. What crosses
/// the boundary (the contracts, the framework) comes from the application, so a type is the same type on both sides; what a
/// dependency plugin loaded is taken from it, so a language built on a platform plugin shares the platform's types; the rest resolves
/// from the plugin's own folder by its <c>.deps.json</c>. Never collectible: plugins load once per start and are not unloaded.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlySet<string> _shared;
    private readonly IReadOnlyList<PluginLoadContext> _dependencies;
    private readonly PluginFiles? _files;

    public PluginLoadContext(string pluginId, string entryAssemblyPath, IReadOnlySet<string> sharedAssemblies,
        IReadOnlyList<PluginLoadContext> dependencies, PluginFiles? files)
        : base($"Invoklet plugin: {pluginId}", isCollectible: false)
    {
        PluginId = pluginId;
        using var manifest = files?.OpenResolverManifest(Path.ChangeExtension(entryAssemblyPath, ".deps.json"));
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _shared = sharedAssemblies;
        _dependencies = dependencies;
        _files = files;
    }

    public string PluginId { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Null hands the request to the default context: the application's copy.
        if (IsShared(assemblyName)) return null;
        foreach (var dependency in _dependencies)
            if (dependency.LoadOwn(assemblyName) is { } fromDependency) return fromDependency;
        return LoadFromOwnFolder(assemblyName);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var dependency in _dependencies)
            if (dependency._resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } fromDependency)
                return dependency.LoadNative(fromDependency);
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadNative(path);
    }

    // An assembly of this plugin or of what it depends on, for a plugin that depends on this one; loaded into the context that owns
    // it, so the dependant sees the very same types the owner uses.
    private Assembly? LoadOwn(AssemblyName assemblyName)
    {
        foreach (var loaded in Assemblies)
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) return loaded;
        foreach (var dependency in _dependencies)
            if (dependency.LoadOwn(assemblyName) is { } fromDependency) return fromDependency;
        return LoadFromOwnFolder(assemblyName);
    }

    private Assembly? LoadFromOwnFolder(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadPluginAssembly(path);
    }

    internal Assembly LoadPluginAssembly(string path)
    {
        if (_files is null) return LoadFromAssemblyPath(path);
        // Loaded from verified bytes, not by path, so Assembly.Location is empty: a plugin finds its files through PluginContext.Directory.
        // The symbols come along when the approved set has them, so a plugin's stack traces keep their line numbers.
        using var assembly = _files.ReadVerified(path);
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        using var symbols = _files.Contains(symbolsPath) ? _files.ReadVerified(symbolsPath) : null;
        return symbols is null ? LoadFromStream(assembly) : LoadFromStream(assembly, symbols);
    }

    private IntPtr LoadNative(string path) => LoadUnmanagedDllFromPath(_files?.GetNativeLoadPath(path) ?? path);

    private bool IsShared(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null) return false;
        return _shared.Contains(name)
            || name == "System" || name == "mscorlib" || name == "netstandard" || name == "WindowsBase"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
    }
}
