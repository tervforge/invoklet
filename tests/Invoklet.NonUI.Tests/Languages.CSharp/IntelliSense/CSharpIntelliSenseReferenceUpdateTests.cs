using Invoklet.Contracts.IntelliSense;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// What the editor does with the answer of the search for assemblies (packages, the target framework): replaces the references it had, and applies the
// answer of the request that is the latest. A resolver that answers from memory stands in for the search, so none of this looks at the disk.
[Trait("Platform", "Independent")]
public sealed class CSharpIntelliSenseReferenceUpdateTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    // An assembly the framework's default references do not have: the mocking library of these tests.
    private static readonly string Extra = typeof(Moq.Mock).Assembly.Location;

    private const string UsesExtra = "var t = typeof(Moq.Mock);";

    private static async Task<IReadOnlyList<string>> ErrorsAsync(CSharpIntelliSenseService service, string code)
    {
        var result = new TaskCompletionSource<IReadOnlyList<IDiagnosticInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDiagnostics(string analyzed, IReadOnlyList<IDiagnosticInfo> diagnostics)
        {
            if (analyzed == code) result.TrySetResult(diagnostics);
        }
        service.DiagnosticsChanged += OnDiagnostics;
        try
        {
            service.NotifyTextChanged(code);
            var diagnostics = await result.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            return [.. diagnostics.Where(d => d.Severity == Invoklet.Contracts.IntelliSense.DiagnosticSeverity.Error).Select(d => d.Message)];
        }
        finally
        {
            service.DiagnosticsChanged -= OnDiagnostics;
        }
    }

    private static PackageReferenceContext Context() => new($"net{Environment.Version.Major}.{Environment.Version.Minor}", null, null);

    [Fact]
    public async Task APackageThatTheSearchFinds_IsReferencedAndOneThatIsNoLongerFound_IsDropped()
    {
        // Arrange: the search finds the library for the package "extra" only.
        using var service = new CSharpIntelliSenseFixture().CreateSut(resolver: new FixedReferenceResolver(
            CSharpIntelliSenseFixture.Framework, packages => packages.ContainsKey("extra") ? [Extra] : []));
        Assert.NotEmpty(await ErrorsAsync(service, UsesExtra));

        // Act and assert
        service.UpdatePackageReferences(new Dictionary<string, Package> { ["extra"] = new("1.0.0") }, Context());
        Assert.Empty(await ErrorsAsync(service, UsesExtra));

        service.UpdatePackageReferences(new Dictionary<string, Package>(), Context());
        Assert.NotEmpty(await ErrorsAsync(service, UsesExtra));
    }

    [Fact]
    public async Task AnOvertakenRequest_AppliesNoneOfItsReferences_WhenTheNewerOneIsAppliedFirst()
    {
        // Arrange: the older request asks for the package "extra", the newer one for none.
        using var service = new CSharpIntelliSenseFixture().CreateSut(resolver: new FixedReferenceResolver(
            CSharpIntelliSenseFixture.Framework, packages => packages.ContainsKey("extra") ? [Extra] : []));
        using var olderHeld = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        service.ExecutionContextResolvedForTests = request =>
        {
            if (!request.Packages.ContainsKey("extra")) return;
            olderHeld.Set();
            Assert.True(release.Wait(Patience));
        };

        // Act: the older one is held after its search and before it applies anything; the newer one is applied whole; then the older one goes on.
        var older = Task.Run(() => service.UpdateExecutionContext(new ExecutionRequest { Packages = new Dictionary<string, string> { ["extra"] = "1.0.0" } }),
            TestContext.Current.CancellationToken);
        Assert.True(await Task.Run(() => olderHeld.Wait(Patience), TestContext.Current.CancellationToken));
        service.UpdateExecutionContext(new ExecutionRequest());
        release.Set();
        await older.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Assert: the references are the newer request's (none), not the older one's.
        Assert.NotEmpty(await ErrorsAsync(service, UsesExtra));
    }

    [Fact]
    public async Task TheSameAnswerTwice_LeavesTheReferencesAsTheyWere()
    {
        using var service = new CSharpIntelliSenseFixture().CreateSut(resolver: new FixedReferenceResolver(CSharpIntelliSenseFixture.Framework, _ => [Extra]));
        service.UpdatePackageReferences(new Dictionary<string, Package> { ["extra"] = new("1.0.0") }, Context());

        service.UpdatePackageReferences(new Dictionary<string, Package> { ["extra"] = new("1.0.0") }, Context());

        Assert.Empty(await ErrorsAsync(service, UsesExtra));
    }
}
