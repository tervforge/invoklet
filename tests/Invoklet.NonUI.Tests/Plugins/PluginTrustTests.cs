using System.Reflection;
using System.Runtime.Loader;
using Invoklet.NonUI.Tests.Plugins.Fixtures;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Category", "Integration")]
public sealed class PluginTrustTests : IDisposable
{
    private const string SampleId = "invoklet.fixture.sample";
    private const string ThemeId = "invoklet.fixture.theme";
    private readonly PluginLoaderFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(false, true, true, PluginDiagnosticCode.PluginNotListed)]
    [InlineData(true, false, true, PluginDiagnosticCode.PluginDisabled)]
    [InlineData(true, true, false, PluginDiagnosticCode.PluginNotApproved)]
    public void Load_Should_RejectUserCodeWithoutAnEnabledApproval(bool listed, bool enabled, bool approved, PluginDiagnosticCode expected)
    {
        AddSample("sample");
        var entry = Assert.Single(_fixture.ApproveDiscovered().Entries) with { Enabled = enabled, Approved = approved };

        var session = _fixture.CreateUserLoader(new PluginInventory(listed ? [entry] : [])).Load();

        Assert.Empty(session.Loaded);
        Assert.Equal(expected, Assert.Single(Assert.Single(session.Rejected).Diagnostics).Code);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Theory]
    [InlineData(PluginContracts.ManifestFileName)]
    [InlineData(PluginLoaderFixture.SampleAssembly)]
    [InlineData("dependency.dll")]
    public void Load_Should_RejectAnApprovedPluginAfterAnyFileChanges(string changedFile)
    {
        var folder = AddSample("sample");
        File.WriteAllText(Path.Combine(folder, "dependency.dll"), "before approval");
        var inventory = _fixture.ApproveDiscovered();
        var discovered = _fixture.Discover();
        File.AppendAllText(Path.Combine(folder, changedFile), " ");

        var session = _fixture.CreateUserLoader(inventory).Load(discovered);

        Assert.Empty(session.Loaded);
        Assert.Equal(PluginDiagnosticCode.PluginContentChanged, Assert.Single(Assert.Single(session.Rejected).Diagnostics).Code);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_UseTheSelectedVersionInTheUserInventory()
    {
        _fixture.AddPlugin(Path.Combine(ThemeId, "1.0.0"), PluginLoaderFixture.ThemeManifest(ThemeId));
        _fixture.AddPlugin(Path.Combine(ThemeId, "2.0.0"), PluginLoaderFixture.ThemeManifest(ThemeId, "2.0.0"));
        var selected = Assert.Single(_fixture.Discover(), plugin => plugin.Manifest!.Version == "2.0.0");

        var session = _fixture.CreateUserLoader(new PluginInventory([PluginLoaderFixture.Approve(selected)])).Load();

        Assert.Equal("2.0.0", Assert.Single(session.Loaded).Manifest.Version);
        Assert.Equal(PluginDiagnosticCode.PluginNotListed, Assert.Single(Assert.Single(session.Rejected).Diagnostics).Code);
        Assert.Single(_fixture.Themes.Registrations);
    }

    [Fact]
    public void Load_Should_RejectAnEntryAssemblyChangedBetweenAdmissionAndLoading()
    {
        AddSample("sample");
        const string secondId = "invoklet.fixture.second";
        var second = _fixture.AddPlugin("second", PluginLoaderFixture.CodeManifest(secondId,
            $"{PluginLoaderFixture.SampleNamespace}.SecondPlugin", version: "2.0.0"), PluginLoaderFixture.SampleAssembly);
        _fixture.Connections.OnApply = registration =>
        {
            if (registration.Context.Manifest.Id == SampleId)
                File.AppendAllText(Path.Combine(second, PluginLoaderFixture.SampleAssembly), "changed during startup");
        };

        var session = _fixture.CreateUserLoader(_fixture.ApproveDiscovered()).Load();

        Assert.Equal(SampleId, Assert.Single(session.Loaded).Id);
        var rejected = Assert.Single(session.Rejected);
        Assert.Equal(secondId, rejected.Id);
        var diagnostic = Assert.Single(rejected.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.EntryAssemblyUnloadable, diagnostic.Code);
        Assert.Contains("changed after verification", diagnostic.Detail);
        Assert.Single(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RejectAChangedDependencyWhenItIsFirstResolvedAfterStartup()
    {
        const string id = "invoklet.fixture.lazy";
        var libraryPath = _fixture.AddPluginWithPrivateLibrary(id, "1.0.0", resolveLibraryInDescriptor: false);
        var session = _fixture.CreateUserLoader(_fixture.ApproveDiscovered()).Load();
        Assert.Equal(id, Assert.Single(session.Loaded).Id);
        var plugin = Assert.Single(_fixture.Connections.Registrations).Plugin!;
        var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
        var assemblyName = Path.GetFileNameWithoutExtension(libraryPath);
        Assert.DoesNotContain(context.Assemblies, assembly => assembly.GetName().Name == assemblyName);
        File.AppendAllText(libraryPath, "changed after startup");

        var error = Assert.ThrowsAny<IOException>(() => context.LoadFromAssemblyName(new AssemblyName(assemblyName)));
        Assert.Contains("changed after verification", error.ToString());
        Assert.DoesNotContain(context.Assemblies, assembly => assembly.GetName().Name == assemblyName);
    }

    [Fact]
    public void Load_Should_KeepBundledIdReservedAndRejectUnlistedBundledCode()
    {
        var bundledRoot = Path.Combine(_fixture.Root, "bundled");
        var bundled = AddSample(Path.Combine("bundled", "sample"));
        _fixture.AddPlugin(Path.Combine("bundled", "theme"), PluginLoaderFixture.ThemeManifest(ThemeId));
        var userRoot = Path.Combine(_fixture.Root, "user");
        AddSample(Path.Combine("user", "sample"));
        var user = Assert.Single(PluginDiscovery.Scan([new PluginSource(userRoot, PluginTrust.Trusted)]));
        var catalogPath = Path.Combine(bundledRoot, BundledPluginCatalog.FileName);
        File.WriteAllText(catalogPath, new BundledPluginCatalog([new(SampleId, "1.0.0", PluginContent.ComputeHash(bundled))]).ToJson());

        var session = _fixture.CreateSut(new PluginLoaderOptions
        {
            ApplicationVersion = _fixture.ApplicationVersion,
            BundledDirectory = bundledRoot,
            BundledCatalog = BundledPluginCatalog.Read(catalogPath),
            UserDirectory = userRoot,
            Inventory = new PluginInventory([PluginLoaderFixture.Approve(user)]),
        }).Load();

        Assert.Equal(PluginTrust.Bundled, Assert.Single(session.Loaded).Trust);
        Assert.Contains(session.Rejected, plugin => plugin.Id == ThemeId && plugin.Diagnostics.Single().Code == PluginDiagnosticCode.PluginNotListed);
        Assert.Contains(session.Rejected, plugin => plugin.Id == SampleId && plugin.Diagnostics.Single().Code == PluginDiagnosticCode.DuplicateId);
        Assert.Single(_fixture.Connections.Registrations);
        Assert.Empty(_fixture.Themes.Registrations);
    }

    [Fact]
    public void Load_Should_FailClosedWhenTheInventoryCannotBeRead()
    {
        AddSample("sample");
        var path = Path.Combine(_fixture.Root, PluginInventory.FileName);
        File.WriteAllText(path, "[{}");

        var session = _fixture.CreateUserLoader(PluginInventory.Read(path)).Load();

        Assert.Empty(session.Loaded);
        Assert.Equal(PluginDiagnosticCode.InventoryUnreadable, Assert.Single(Assert.Single(session.Rejected).Diagnostics).Code);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_UnconfiguredDevelopmentRoot_RejectsTheCandidate()
    {
        var folder = AddSample("sample");
        var source = new PluginSource(folder, PluginTrust.Development);
        var discovered = PluginDiscovery.Scan([source]);

        var session = _fixture.CreateUserLoader(PluginInventory.Empty).Load(discovered);

        Assert.Empty(session.Loaded);
        Assert.Equal(PluginDiagnosticCode.PluginSourceNotConfigured, Assert.Single(Assert.Single(session.Rejected).Diagnostics).Code);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_ExplicitDevelopmentRoot_LoadsUnlistedCodeWithAWarning()
    {
        var folder = AddSample("sample");
        var options = new PluginLoaderOptions { ApplicationVersion = _fixture.ApplicationVersion, DevelopmentDirectory = folder };

        var session = _fixture.CreateSut(options).Load();

        Assert.Equal(PluginTrust.Development, Assert.Single(session.Loaded).Trust);
        var warning = Assert.Single(session.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.DevelopmentPlugin, warning.Code);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void Session_Should_CompareInventoryWithStartupWithoutChangingLoadedPlugins()
    {
        _fixture.AddPlugin("theme", PluginLoaderFixture.ThemeManifest(ThemeId));
        AddSample("sample");
        var entries = _fixture.ApproveDiscovered().Entries;
        var sample = Assert.Single(entries, entry => entry.Id == SampleId);
        var theme = Assert.Single(entries, entry => entry.Id == ThemeId);
        var path = Path.Combine(_fixture.Root, PluginInventory.FileName);
        var startup = new PluginInventory([sample, theme with { Approved = false }]);
        File.WriteAllText(path, startup.ToJson());
        var session = _fixture.CreateUserLoader(PluginInventory.Read(path)).Load();

        Assert.Empty(session.GetPendingRestart(startup));
        File.WriteAllText(path, new PluginInventory([theme, sample with { Enabled = false }]).ToJson());
        Assert.Equal([SampleId, ThemeId], session.GetPendingRestart(PluginInventory.Read(path)));
        Assert.Equal([SampleId, ThemeId], session.GetPendingRestart(new PluginInventory([theme with { Version = "2.0.0", ContentHash = new string('f', 64) }])));
        Assert.Equal(SampleId, Assert.Single(session.Loaded).Id);
        Assert.Equal(ThemeId, Assert.Single(session.Rejected).Id);
        Assert.Single(_fixture.Connections.Registrations);
        Assert.Empty(_fixture.Themes.Registrations);
    }

    [Fact]
    public void ComputeHash_Should_BeIndependentOfRootAndIncludeRelativeNames()
    {
        var first = _fixture.AddPlugin("one", PluginLoaderFixture.ThemeManifest(ThemeId));
        var second = _fixture.AddPlugin("two", PluginLoaderFixture.ThemeManifest(ThemeId));
        File.WriteAllText(Path.Combine(first, "theme.json"), "{}");
        File.WriteAllText(Path.Combine(second, "theme.json"), "{}");
        Assert.Equal(PluginContent.ComputeHash(first), PluginContent.ComputeHash(second));

        File.Move(Path.Combine(second, "theme.json"), Path.Combine(second, "editor.json"));

        Assert.NotEqual(PluginContent.ComputeHash(first), PluginContent.ComputeHash(second));
    }

    private string AddSample(string path) => _fixture.AddPlugin(path,
        PluginLoaderFixture.CodeManifest(SampleId, $"{PluginLoaderFixture.SampleNamespace}.SamplePlugin"), PluginLoaderFixture.SampleAssembly);

}
