using Invoklet.Languages.CSharp;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins.Fixtures;

/// <summary>
/// A temporary plugin source the tests fill with folders: a manifest each, and the fixture assemblies built beside the test assembly
/// (<c>tests/fixtures/plugins</c>) copied in. The handlers record what the loader registers.
/// </summary>
public sealed class PluginLoaderFixture : IDisposable
{
    public const string SampleAssembly = "Invoklet.Plugins.Fixtures.Sample.dll";
    public const string LibraryAssembly = "Invoklet.Plugins.Fixtures.Library.dll";
    public const string DependentAssembly = "Invoklet.Plugins.Fixtures.Dependent.dll";
    public const string SampleNamespace = "Invoklet.Plugins.Fixtures.Sample";

    public string Root { get; } = Directory.CreateTempSubdirectory("Invoklet-plugin-loader").FullName;

    public PluginSource Source => new(Root, PluginTrust.Trusted);

    public Version ApplicationVersion { get; init; } = new(1, 2, 3);

    public RecordingHandler Connections { get; } = new(PluginRole.Connection, PluginContracts.Connection);

    public RecordingHandler Themes { get; } = new(PluginRole.Theme, PluginContracts.Theme);

    // Best effort: a plugin context is never unloaded, so on Windows the assemblies a test loaded stay mapped, and their folder
    // can't go until the test process ends. What can be deleted is; the rest goes with the temp folder.
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A plugin folder with the manifest text given and the fixture assemblies named copied beside it.</summary>
    public string AddPlugin(string folder, string manifest, params string[] assemblies)
    {
        var directory = Path.Combine(Root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, PluginContracts.ManifestFileName), manifest);
        foreach (var assembly in assemblies)
            File.Copy(Path.Combine(AppContext.BaseDirectory, assembly), Path.Combine(directory, assembly));
        return directory;
    }

    /// <summary>A manifest for a plugin with code: the sample assembly's type named, in the connection role unless said otherwise.</summary>
    public static string CodeManifest(string id, string type, string version = "1.0.0", string roles = "\"connection\": 1",
        string assembly = SampleAssembly, string? dependsOn = null, string? minAppVersion = null) =>
        $$"""
        {
          "id": "{{id}}",
          "version": "{{version}}",
          "displayName": "{{id}}",
          "roles": { {{roles}} },
          "entry": { "assembly": "{{assembly}}", "type": "{{type}}" }{{(dependsOn is null ? "" : $", \"dependsOn\": [{dependsOn}]")}}{{(minAppVersion is null ? "" : $", \"minAppVersion\": \"{minAppVersion}\"")}}
        }
        """;

    public static string ThemeManifest(string id, string version = "1.0.0", string contractVersion = "1") =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "displayName": "{{id}}", "roles": { "theme": {{contractVersion}} } }""";

    public string AddPluginWithPrivateLibrary(string id, string version, bool resolveLibraryInDescriptor = true)
    {
        const string entry = "VersionedPlugin";
        const string library = "PrivatePluginLibrary";
        var directory = AddPlugin(id, CodeManifest(id, entry, version, assembly: $"{entry}.dll"));
        var libraryPath = Path.Combine(directory, $"{library}.dll");
        var framework = new CSharpReferenceService().GetDefault().Select(path => MetadataReference.CreateFromFile(path)).ToArray();

        EmitLibrary(libraryPath, $$"""
            [assembly: System.Reflection.AssemblyVersion("{{version}}")]
            public sealed class PrivateLibraryMarker { }
            """, framework);
        var descriptorVersion = resolveLibraryInDescriptor ? "typeof(PrivateLibraryMarker).Assembly.GetName().Version.ToString(3)" : $"\"{version}\"";
        EmitLibrary(Path.Combine(directory, $"{entry}.dll"), $$"""
            using Invoklet.Plugins;
            public sealed class {{entry}} : IInvokletPlugin
            {
                public PluginDescriptor Descriptor => new("{{id}}", {{descriptorVersion}});
            }
            """, [.. framework, MetadataReference.CreateFromFile(libraryPath),
                MetadataReference.CreateFromFile(typeof(IInvokletPlugin).Assembly.Location)]);

        File.WriteAllText(Path.Combine(directory, $"{entry}.deps.json"), $$"""
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "{{entry}}/1.0.0": { "runtime": { "{{entry}}.dll": {} } },
                  "{{library}}/{{version}}": { "runtime": { "{{library}}.dll": {} } }
                }
              },
              "libraries": {
                "{{entry}}/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                "{{library}}/{{version}}": { "type": "project", "serviceable": false, "sha512": "" }
              }
            }
            """);
        return libraryPath;
    }

    private static void EmitLibrary(string path, string source, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    public PluginLoader CreateSut(params IPluginRoleHandler[] handlers) =>
        new(new PluginLoaderOptions { ApplicationVersion = ApplicationVersion, UserDirectory = Root, Inventory = ApproveDiscovered() },
            handlers.Length == 0 ? [Connections, Themes] : handlers, NullLoggerFactory.Instance);

    public PluginLoader CreateSut(PluginLoaderOptions options) =>
        new(options, [Connections, Themes], NullLoggerFactory.Instance);

    public PluginLoader CreateUserLoader(PluginInventory inventory) => CreateSut(new PluginLoaderOptions
    {
        ApplicationVersion = ApplicationVersion,
        UserDirectory = Root,
        Inventory = inventory,
    });

    public IReadOnlyList<DiscoveredPlugin> Discover() => PluginDiscovery.Scan([Source]);

    public PluginInventory ApproveDiscovered() => new(Discover().Where(plugin => plugin.Manifest is not null)
        .DistinctBy(plugin => plugin.Id).Select(Approve));

    public static PluginInventoryEntry Approve(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest!;
        return new PluginInventoryEntry(manifest.Id, manifest.Version, manifest.Roles.Keys.Select(PluginRoles.Name).ToArray(),
            "fixture", manifest.Publisher, PluginContent.ComputeHash(plugin.Directory), "none", Enabled: true, Approved: true);
    }

    public sealed class RecordingHandler(PluginRole role, int contractVersion) : IPluginRoleHandler
    {
        public PluginRole Role => role;
        public int ContractVersion => contractVersion;
        public List<PluginRegistration> Registrations { get; } = [];
        public Exception? Throws { get; set; }
        public Action<PluginRegistration>? OnApply { get; set; }

        public PluginRoleRegistration Prepare(PluginRegistration registration) => new(
            () =>
            {
                Registrations.Add(registration);
                OnApply?.Invoke(registration);
                if (Throws is not null) throw Throws;
            },
            () => Registrations.Remove(registration));
    }
}
