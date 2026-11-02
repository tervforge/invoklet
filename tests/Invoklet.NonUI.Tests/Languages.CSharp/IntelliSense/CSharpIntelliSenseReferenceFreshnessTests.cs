using Invoklet.Contracts.Connections;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.CSharp.IntelliSense.ScriptDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// What the editor offers has to be what the build compiles against: a library built again in the same place is read again, and
// a context request that is overtaken applies nothing of itself (not its references either).
public sealed class CSharpIntelliSenseReferenceFreshnessTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly string Tfm = $"net{Environment.Version.Major}.{Environment.Version.Minor}";

    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-reference-freshness").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static CSharpIntelliSenseService Create()
    {
        var references = new CSharpReferenceService();
        return new CSharpIntelliSenseService(
            references.GetDefault().Append(references.GetExtensionReference()), NullLogger<CSharpIntelliSenseService>.Instance)
        {
            BackgroundAnalysisDebounce = TimeSpan.Zero,
        };
    }

    // A library with one public class; written again to the same path it has another size and time, as a rebuilt one does.
    private static void BuildLibrary(string path, string typeName, int modifiedSecondsAgo)
    {
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText($"namespace Lib {{ public static class {typeName} {{ }} }}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-modifiedSecondsAgo));
    }

    private static async Task<List<string>> OfferedAsync(CSharpIntelliSenseService service, string code)
    {
        var completions = await service.GetCompletionsAsync(code, code.Length, ct: TestContext.Current.CancellationToken);
        return [.. completions?.Items.Select(i => i.DisplayText) ?? []];
    }

    private static PackageReferenceContext Context(string library) => new(Tfm, null, null, "dotnet", [library]);

    [Fact]
    public async Task ALibraryBuiltAgainInTheSamePlace_IsOfferedAsItIsNow()
    {
        // Arrange
        using var service = Create();
        var library = Path.Combine(_folder, "Lib.dll");
        BuildLibrary(library, "First", modifiedSecondsAgo: 60);
        service.UpdatePackageReferences(new Dictionary<string, Package>(), Context(library));
        Assert.Contains("First", await OfferedAsync(service, "Lib."));

        // Act
        BuildLibrary(library, "Second", modifiedSecondsAgo: 0);
        service.UpdatePackageReferences(new Dictionary<string, Package>(), Context(library));

        // Assert
        var offered = await OfferedAsync(service, "Lib.");
        Assert.Contains("Second", offered);
        Assert.DoesNotContain("First", offered);
    }

    [Fact]
    public void CreateReference_AFileThatDidNotChange_IsTheSameReferenceAgain()
    {
        var library = Path.Combine(_folder, "Same.dll");
        BuildLibrary(library, "Only", modifiedSecondsAgo: 60);

        Assert.Same(ScriptContext.CreateReference(library), ScriptContext.CreateReference(library));
    }

    [Fact]
    public async Task ASlowOlderRequest_Should_ApplyNoneOfItsReferences_WhenANewerOneIsAppliedFirst()
    {
        // Arrange
        using var service = Create();
        var older = Path.Combine(_folder, "Older.dll");
        var newer = Path.Combine(_folder, "Newer.dll");
        BuildLibrary(older, "FromOlder", modifiedSecondsAgo: 60);
        BuildLibrary(newer, "FromNewer", modifiedSecondsAgo: 60);
        using var olderHeld = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        service.ExecutionContextResolvedForTests = request =>
        {
            if (!request.References.Contains(older)) return;
            olderHeld.Set();
            Assert.True(release.Wait(Patience));
        };
        ExecutionRequest Request(string library, string member) => new()
        {
            Platform = new DotnetSdk(Tfm, Tfm, Tfm[3..] + ".0", Tfm),
            References = [library],
            Connections = new GeneratedConnections($"public static partial class Connections {{ public static int {member} => 1; }}", [], []),
        };

        // Act: the older request is held before it applies anything; the newer one is applied whole; then the older one goes on.
        var slow = Task.Run(() => service.UpdateExecutionContext(Request(older, "Stock")), TestContext.Current.CancellationToken);
        Assert.True(await Task.Run(() => olderHeld.Wait(Patience), TestContext.Current.CancellationToken));
        service.UpdateExecutionContext(Request(newer, "Audit"));
        release.Set();
        await slow.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Assert: references and connections are all of the newer request.
        var libraryTypes = await OfferedAsync(service, "Lib.");
        Assert.Contains("FromNewer", libraryTypes);
        Assert.DoesNotContain("FromOlder", libraryTypes);
        var connections = await OfferedAsync(service, "Connections.");
        Assert.Contains("Audit", connections);
        Assert.DoesNotContain("Stock", connections);
    }
}
