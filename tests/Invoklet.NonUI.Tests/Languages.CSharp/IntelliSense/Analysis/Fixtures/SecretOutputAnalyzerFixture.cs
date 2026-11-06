using Invoklet.Contracts.Connections;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Contracts.Models;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Analysis.Fixtures;

// The real IntelliSense service, with Invoklet.Runtime referenced as the editor has it, analysing at once; what it reports for a text
// is caught as it comes.
internal sealed class SecretOutputAnalyzerFixture
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public CSharpIntelliSenseService CreateSut()
    {
        var references = new CSharpReferenceService();
        return new CSharpIntelliSenseService(
            references.GetDefault().Append(references.GetExtensionReference()), NullLogger<CSharpIntelliSenseService>.Instance, null,
            new FixedReferenceResolver(CSharpIntelliSenseFixture.Framework))
        {
            BackgroundAnalysisDebounce = TimeSpan.Zero,
        };
    }

    // A database connection 'shop' whose connection string holds the secret 'shop-db', as Invoklet generates it.
    public static ExecutionRequest WithSecretConnection() => new()
    {
        Connections = new GeneratedConnections("""
            public static partial class Connections
            {
                public static ShopClient Shop => new ShopClient();
                public sealed class ShopClient
                {
                    internal ShopClient()
                    {
                        ConnectionString = "Host=db;Password=" + global::Util.GetSecret("shop-db");
                    }
                    public string ConnectionString { get; }
                }
            }
            """, [], []),
    };

    /// <summary>The warnings about a secret written to the output, once <paramref name="sut"/> has analysed <paramref name="code"/>.</summary>
    public static Task<IReadOnlyList<IDiagnosticInfo>> SecretWarnings(CSharpIntelliSenseService sut, string code)
    {
        var analysed = new TaskCompletionSource<IReadOnlyList<IDiagnosticInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsChanged += (text, diagnostics) =>
        {
            if (text == code)
                analysed.TrySetResult([.. diagnostics.Where(d => d.Message.Contains("is written to the output", StringComparison.Ordinal))]);
        };
        return analysed.Task;
    }

    /// <summary>The fix of that title, once <paramref name="sut"/> offers it for <paramref name="code"/>.</summary>
    public static Task<ICodeFixInfo> Fix(CSharpIntelliSenseService sut, string code, string title)
    {
        var listed = new TaskCompletionSource<ICodeFixInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.CodeFixesChanged += (analysed, fixes) =>
        {
            if (analysed == code && fixes.FirstOrDefault(f => f.Title == title) is { } fix) listed.TrySetResult(fix);
        };
        return listed.Task;
    }
}
