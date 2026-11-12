using System.Runtime.Loader;
using Invoklet.NonUI.Tests.Plugins.Fixtures;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Category", "Integration")]
public sealed class PluginLoaderTests : IDisposable
{
    private const string Sample = PluginLoaderFixture.SampleNamespace;

    private readonly PluginLoaderFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private PluginSession Load() => _fixture.CreateSut().Load(_fixture.Discover());

    private static PluginDiagnostic TheError(RejectedPlugin plugin) => Assert.Single(plugin.Diagnostics, d => d.IsError);

    [Fact]
    public void Load_Should_LoadAPlugin_InitializeIt_AndRegisterItsRole()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var loaded = Assert.Single(session.Loaded);
        Assert.Equal("invoklet.fixture.sample", loaded.Id);
        Assert.Equal(PluginTrust.Trusted, loaded.Trust);
        Assert.Empty(session.Rejected);
        var registration = Assert.Single(_fixture.Connections.Registrations);
        Assert.Equal("invoklet.fixture.sample", registration.Plugin!.Descriptor.Id);
        Assert.Same(loaded.Manifest, registration.Context.Manifest);
        Assert.Equal(loaded.Directory, registration.Context.Directory);
        Assert.Equal("Invoklet plugin: invoklet.fixture.sample", AssemblyLoadContext.GetLoadContext(registration.Plugin.GetType().Assembly)!.Name);
        Assert.True(session.IsLoaded("invoklet.fixture.sample"));
    }

    [Fact]
    public void Load_Should_GiveAPlugin_TheApplicationsContractAssembly_EvenWhenItShipsItsOwnCopy()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly, "Invoklet.Plugins.dll");

        var session = Load();

        Assert.Single(session.Loaded);
        var plugin = Assert.Single(_fixture.Connections.Registrations).Plugin!;
        var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
        Assert.DoesNotContain(context.Assemblies, a => a.GetName().Name == "Invoklet.Plugins");
        Assert.Same(typeof(IInvokletPlugin).Assembly, plugin.GetType().GetInterfaces().Single().Assembly);
    }

    [Fact]
    public void Load_Should_RejectAPluginThatThrowsWhileInitializing_AndKeepTheOthers()
    {
        _fixture.AddPlugin("a-throwing", PluginLoaderFixture.CodeManifest("invoklet.fixture.throwing", $"{Sample}.ThrowingPlugin"),
            PluginLoaderFixture.SampleAssembly);
        _fixture.AddPlugin("b-sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.Equal("invoklet.fixture.sample", Assert.Single(session.Loaded).Id);
        var rejected = Assert.Single(session.Rejected);
        Assert.Equal("invoklet.fixture.throwing", rejected.Id);
        var error = TheError(rejected);
        Assert.Equal(PluginDiagnosticCode.RegistrationFailed, error.Code);
        Assert.Contains("always fails", error.Detail);
        Assert.DoesNotContain("always fails", error.Message);
        Assert.Equal("invoklet.fixture.sample", Assert.Single(_fixture.Connections.Registrations).Plugin!.Descriptor.Id);
    }

    // Assemblies load from verified bytes, so the symbols must be handed over too, or a plugin's stack traces lose their lines.
    [Fact]
    public void Load_Should_LoadTheSymbolsBesideAnApprovedAssembly()
    {
        _fixture.AddThrowingPluginWithSymbols("invoklet.fixture.symbols");

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.RegistrationFailed, error.Code);
        Assert.Contains(PluginLoaderFixture.SymbolsSourceFile, error.Detail);
    }

    [Fact]
    public void Load_Should_RejectAPluginWhoseHandlerThrows()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly);
        _fixture.Connections.Throws = new InvalidOperationException("A connection driver 'x' is already registered.");

        var session = Load();

        Assert.Empty(session.Loaded);
        Assert.Empty(_fixture.Connections.Registrations);
        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.RegistrationFailed, error.Code);
        Assert.Contains("register as a connection", error.Message);
    }

    [Fact]
    public void Load_Should_RollBackAllRoles_WhenALaterRegistrationFails()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin",
                roles: "\"connection\": 1, \"theme\": 1"), PluginLoaderFixture.SampleAssembly);
        _fixture.AddPlugin("second", PluginLoaderFixture.CodeManifest("invoklet.fixture.second", $"{Sample}.SecondPlugin", version: "2.0.0"),
            PluginLoaderFixture.SampleAssembly);
        _fixture.Themes.Throws = new InvalidOperationException("A theme failed after registering.");

        var session = Load();

        Assert.Equal("invoklet.fixture.sample", Assert.Single(session.Rejected).Id);
        Assert.Empty(_fixture.Themes.Registrations);
        Assert.Equal("invoklet.fixture.second", Assert.Single(_fixture.Connections.Registrations).Plugin!.Descriptor.Id);
        Assert.Equal("invoklet.fixture.second", Assert.Single(session.Loaded).Id);
    }

    [Fact]
    public void Load_Should_KeepTheFirstOfADuplicateId_AndRejectTheSecond()
    {
        _fixture.AddPlugin("a-first", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly);
        _fixture.AddPlugin("b-second", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.EndsWith("a-first", Assert.Single(session.Loaded).Directory);
        var rejected = Assert.Single(session.Rejected);
        Assert.EndsWith("b-second", rejected.Directory);
        Assert.Equal(PluginDiagnosticCode.DuplicateId, TheError(rejected).Code);
        Assert.Single(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RejectAnotherContractVersion_BeforeLoadingAnyCode()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", roles: "\"connection\": 2"));

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.UnsupportedContractVersion, error.Code);
        Assert.Contains("version 2 of the connection contract", error.Message);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RejectARoleWithoutAHandler()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", roles: "\"language\": 1"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.UnsupportedRole, error.Code);
        Assert.Contains("'language'", error.Message);
    }

    [Fact]
    public void Load_Should_RejectAPluginThatNeedsANewerInvoklet()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", minAppVersion: "1.3.0"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.ApplicationTooOld, error.Code);
        Assert.Contains("1.3.0", error.Message);
        Assert.Contains("1.2.3", error.Message);
    }

    [Fact]
    public void Load_Should_AcceptAPluginWhoseMinimumIsThisInvoklet()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", minAppVersion: "1.2.3"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.Single(session.Loaded);
    }

    [Fact]
    public void Load_Should_RejectAPluginWhoseDependencyIsMissing()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", dependsOn: "\"invoklet.fixture.absent\""),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.DependencyMissing, error.Code);
        Assert.Contains("'invoklet.fixture.absent'", error.Message);
    }

    [Fact]
    public void Load_Should_RejectEveryPluginOnACycle()
    {
        _fixture.AddPlugin("a", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", dependsOn: "\"invoklet.fixture.second\""),
            PluginLoaderFixture.SampleAssembly);
        _fixture.AddPlugin("b", PluginLoaderFixture.CodeManifest("invoklet.fixture.second", $"{Sample}.SecondPlugin", version: "2.0.0", dependsOn: "\"invoklet.fixture.sample\""),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.Empty(session.Loaded);
        Assert.Equal(2, session.Rejected.Count);
        Assert.Contains(session.Rejected, p => TheError(p).Code == PluginDiagnosticCode.DependencyCycle);
        Assert.All(session.Rejected, p => Assert.True(TheError(p).Code is PluginDiagnosticCode.DependencyCycle or PluginDiagnosticCode.DependencyRejected));
    }

    [Fact]
    public void Load_Should_LoadADependencyFirst_AndShareItsTypesWithTheDependant()
    {
        _fixture.AddPlugin("z-dependent", PluginLoaderFixture.CodeManifest("invoklet.fixture.dependent", "Invoklet.Plugins.Fixtures.Dependent.DependentPlugin",
                assembly: PluginLoaderFixture.DependentAssembly, dependsOn: "\"invoklet.fixture.library\""),
            PluginLoaderFixture.DependentAssembly);
        _fixture.AddPlugin("a-library", PluginLoaderFixture.CodeManifest("invoklet.fixture.library", "Invoklet.Plugins.Fixtures.Library.LibraryPlugin",
                assembly: PluginLoaderFixture.LibraryAssembly, roles: "\"library\": 1"),
            PluginLoaderFixture.LibraryAssembly);

        var session = Load();

        Assert.Equal(["invoklet.fixture.library", "invoklet.fixture.dependent"], session.Loaded.Select(p => p.Id));
        var dependent = Assert.Single(_fixture.Connections.Registrations).Plugin!;
        var baseType = dependent.GetType().BaseType!;
        Assert.Equal("Invoklet.Plugins.Fixtures.Library", baseType.Assembly.GetName().Name);
        Assert.Equal("Invoklet plugin: invoklet.fixture.library", AssemblyLoadContext.GetLoadContext(baseType.Assembly)!.Name);
    }

    [Fact]
    public void Load_Should_KeepDifferentVersionsOfAPrivateLibraryIsolated()
    {
        _fixture.AddPluginWithPrivateLibrary("invoklet.fixture.first", "1.0.0");
        _fixture.AddPluginWithPrivateLibrary("invoklet.fixture.second", "2.0.0");

        var session = Load();

        Assert.Empty(session.Rejected);
        Assert.Equal(["1.0.0", "2.0.0"], _fixture.Connections.Registrations.Select(r => r.Plugin!.Descriptor.Version));
    }

    [Fact]
    public void Load_Should_RejectTheDependantsOfAPluginThatFailed()
    {
        _fixture.AddPlugin("a-library", PluginLoaderFixture.CodeManifest("invoklet.fixture.throwing", $"{Sample}.ThrowingPlugin", roles: "\"library\": 1"),
            PluginLoaderFixture.SampleAssembly);
        _fixture.AddPlugin("b-dependent", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin", dependsOn: "\"invoklet.fixture.throwing\""),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.Empty(session.Loaded);
        var dependant = Assert.Single(session.Rejected, p => p.Id == "invoklet.fixture.sample");
        Assert.Equal(PluginDiagnosticCode.DependencyRejected, TheError(dependant).Code);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RejectAPluginWithoutItsEntryAssembly()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.SamplePlugin"));

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.EntryAssemblyMissing, error.Code);
        Assert.DoesNotContain(_fixture.Root, error.Message);
    }

    [Fact]
    public void Load_Should_RejectAPluginWhoseEntryTypeIsNotThere()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.NoSuchPlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        Assert.Equal(PluginDiagnosticCode.EntryTypeMissing, TheError(Assert.Single(session.Rejected)).Code);
    }

    [Fact]
    public void Load_Should_RejectAnEntryTypeThatIsNotAPlugin()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.sample", $"{Sample}.NotAPlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.EntryTypeInvalid, error.Code);
        Assert.Contains("does not implement IInvokletPlugin", error.Message);
    }

    [Fact]
    public void Load_Should_RejectAPluginWhoseCodeContradictsItsManifest()
    {
        _fixture.AddPlugin("sample", PluginLoaderFixture.CodeManifest("invoklet.fixture.mismatched", $"{Sample}.MismatchedPlugin"),
            PluginLoaderFixture.SampleAssembly);

        var session = Load();

        var error = TheError(Assert.Single(session.Rejected));
        Assert.Equal(PluginDiagnosticCode.DescriptorMismatch, error.Code);
        Assert.Contains("invoklet.fixture.other 9.9.9", error.Detail);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RegisterAThemeWithoutLoadingAnyCode()
    {
        _fixture.AddPlugin("midnight", PluginLoaderFixture.ThemeManifest("invoklet.theme.midnight"));

        var session = Load();

        Assert.Single(session.Loaded);
        var registration = Assert.Single(_fixture.Themes.Registrations);
        Assert.Null(registration.Plugin);
        Assert.Equal("invoklet.theme.midnight", registration.Context.Manifest.Id);
        Assert.Empty(_fixture.Connections.Registrations);
    }

    [Fact]
    public void Load_Should_RejectCodeOnADataOnlyManifest_EvenWhenBuiltInMemory()
    {
        var directory = _fixture.AddPlugin("sample", PluginLoaderFixture.ThemeManifest("invoklet.fixture.sample"),
            PluginLoaderFixture.SampleAssembly);
        var candidate = Assert.Single(_fixture.Discover());
        var manifest = candidate.Manifest! with { Entry = new PluginEntryPoint(PluginLoaderFixture.SampleAssembly, $"{Sample}.SamplePlugin") };

        var session = _fixture.CreateSut().Load([candidate with { Manifest = manifest }]);

        Assert.Empty(session.Loaded);
        Assert.Empty(_fixture.Themes.Registrations);
        Assert.Equal(directory, Assert.Single(session.Rejected).Directory);
        Assert.Equal(PluginDiagnosticCode.ManifestInvalid, TheError(Assert.Single(session.Rejected)).Code);
    }

    [Fact]
    public void Session_Should_KeepItsSnapshotAfterTheSourceListsChange()
    {
        var manifest = PluginManifestReader.Read(PluginLoaderFixture.ThemeManifest("invoklet.fixture.sample")).Manifest!;
        var diagnostics = new List<PluginDiagnostic>
        {
            PluginDiagnostic.Warning(manifest.Id, PluginDiagnosticCode.ManifestInvalid, "Synthetic warning"),
        };
        var loaded = new List<LoadedPlugin> { new(manifest, _fixture.Root, PluginTrust.Trusted, diagnostics) };
        var session = new PluginSession(loaded, []);

        loaded.Clear();
        diagnostics.Clear();

        Assert.True(session.IsLoaded(manifest.Id));
        Assert.Single(session.Diagnostics);
        Assert.Single(Assert.Single(session.Loaded).Diagnostics);
    }

    [Fact]
    public void Load_Should_RejectAThemeOfAnotherContractVersion()
    {
        _fixture.AddPlugin("midnight", PluginLoaderFixture.ThemeManifest("invoklet.theme.midnight", contractVersion: "7"));

        var session = Load();

        Assert.Equal(PluginDiagnosticCode.UnsupportedContractVersion, TheError(Assert.Single(session.Rejected)).Code);
    }

    [Fact]
    public void Load_Should_CarryAnUnreadableManifest_IntoTheRejectedList()
    {
        var folder = Path.Combine(_fixture.Root, "broken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PluginContracts.ManifestFileName), "{ \"id\": \"Bad Id\" }");

        var session = Load();

        var rejected = Assert.Single(session.Rejected);
        Assert.Equal("broken", rejected.Id);
        Assert.Null(rejected.Manifest);
        Assert.Contains(rejected.Diagnostics, d => d.Code == PluginDiagnosticCode.ManifestInvalid);
        Assert.Equal(rejected.Diagnostics, session.Diagnostics);
    }

    [Fact]
    public void Load_Should_RefuseTwoHandlersOfOneRole()
    {
        var duplicate = new PluginLoaderFixture.RecordingHandler(PluginRole.Connection, 1);

        Assert.Throws<ArgumentException>(() => _fixture.CreateSut(_fixture.Connections, duplicate));
    }
}
