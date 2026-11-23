using System.Collections.Concurrent;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Contracts.Models;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense;

// The editor's execution context (references, generated connection classes) is replaced from a
// worker thread while the user keeps typing: a request that is overtaken must not be applied over
// the newer one, and analysis of an obsolete solution must not reach the editor.
[Trait("Platform", "Independent")]
public sealed class CSharpIntelliSenseContextUpdateTests
{
    private static CSharpIntelliSenseService Create()
    {
        var references = new CSharpReferenceService();
        return new CSharpIntelliSenseService(
            references.GetDefault().Append(references.GetExtensionReference()), NullLogger<CSharpIntelliSenseService>.Instance, null,
            new FixedReferenceResolver(CSharpIntelliSenseFixture.Framework))
        {
            BackgroundAnalysisDebounce = TimeSpan.Zero,
        };
    }

    private static ExecutionRequest Connection(string member) => new()
    {
        Connections = new GeneratedConnections(
            $"public static partial class Connections {{ public static int {member} => 1; }}", [], []),
    };

    private static async Task<bool> OffersAsync(CSharpIntelliSenseService service, string member)
    {
        const string code = "Connections.";
        var completions = await service.GetCompletionsAsync(code, code.Length, ct: TestContext.Current.CancellationToken);
        return completions?.Items.Any(i => i.DisplayText == member) == true;
    }

    [Fact]
    public async Task QueryMemberCompletion_Should_UseTheGeneratedConnectionReceiver()
    {
        using var service = Create();
        const string code = "var q = Connections.Db.Users.Where(u => u.Id == 1);\nq.";
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await service.GetCompletionsAsync(code, code.Length, CompletionTrigger.Insertion('.'), ct));
        Assert.Null(await service.GetCompletionsAsync(code, code.Length, CompletionTrigger.Invoked, ct));

        await service.UpdateExecutionContextAsync(new ExecutionRequest
        {
            Connections = new GeneratedConnections("""
                public static partial class Connections { public static Database Db => new(); }
                public sealed class Database { public System.Linq.IQueryable<User> Users => throw new System.NotImplementedException(); }
                public sealed class User { public int Id { get; set; } }
                """, [], []),
        });

        var list = await service.GetCompletionsAsync(code, code.Length, CompletionTrigger.Insertion('.'), ct);
        Assert.NotNull(list);
        Assert.Contains(list.Items, i => i.DisplayText == "Provider");
        Assert.Contains(list.Items, i => i.DisplayText == "Select");
        Assert.DoesNotContain(list.Items, i => i.DisplayText == "Connections");
    }

    [Fact]
    public async Task ACancelledRequest_Should_ChangeNothing()
    {
        using var service = Create();
        await service.UpdateExecutionContextAsync(Connection("Stock"));
        Assert.True(await OffersAsync(service, "Stock"));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyExecutionContextAsync(Connection("Audit"), cancelled.Token));

        Assert.True(await OffersAsync(service, "Stock"));
        Assert.False(await OffersAsync(service, "Audit"));
    }

    // The pass has its solution snapshot when the classes of "Connections" arrive. What it computes
    // describes a solution without them (an unknown name has no class colour), so it must not be
    // published; the pass runs again on the new solution instead.
    [Fact]
    public async Task AnalysisOfAnObsoleteSolution_Should_NotBePublished_AndRunAgain()
    {
        using var service = Create();
        const string code = "Connections.Stock.ToString();";
        var published = new ConcurrentQueue<IReadOnlyList<ISemanticToken>>();
        var current = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SemanticTokensChanged += (_, tokens) =>
        {
            published.Enqueue(tokens);
            if (IsClassColoured(tokens)) current.TrySetResult();
        };

        var snapshotTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        service.AnalysisSnapshotTakenForTests = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Task.CompletedTask;
            snapshotTaken.TrySetResult();
            return proceed.Task;
        };

        service.NotifyTextChanged(code);
        await snapshotTaken.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await service.UpdateExecutionContextAsync(new ExecutionRequest
        {
            Connections = new GeneratedConnections("public static partial class Connections { public static int Stock => 1; }", [], []),
        });
        proceed.TrySetResult();

        await current.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.All(published, tokens => Assert.True(IsClassColoured(tokens), "an obsolete analysis was published"));
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ApplyExecutionContextAsync_Should_ReplaceLightbulbKeys_WithoutATextEdit()
    {
        using var service = Create();
        const string code = "var match = Regex.Match(\"a\", \"a\");";
        var passes = System.Threading.Channels.Channel.CreateUnbounded<IReadOnlyList<ICodeFixInfo>>();
        service.CodeFixesChanged += (_, fixes) => passes.Writer.TryWrite(fixes);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Patience);
        service.NotifyTextChanged(code);
        var oldFix = Assert.Single(await passes.Reader.ReadAsync(timeout.Token), f => f.Title == "using System.Text.RegularExpressions;");

        await service.ApplyExecutionContextAsync(Connection("Stock"), timeout.Token);

        var newFix = Assert.Single(await passes.Reader.ReadAsync(timeout.Token), f => f.Title == oldFix.Title);
        Assert.NotEqual(oldFix.Key, newFix.Key);
        Assert.Null(await service.MaterializeFixAsync(code, oldFix.Key, timeout.Token));
        var applied = await service.MaterializeFixAsync(code, newFix.Key, timeout.Token);
        Assert.NotNull(applied);
        Assert.Contains("System.Text.RegularExpressions", applied.UsingsToAdd);
    }

    // The older request is the slow one (its lookups were still running): it finishes after the
    // newer has been applied, and, not cancelled by anyone, must still not replace it.
    [Fact]
    public async Task ASlowOlderRequest_Should_NotOverwriteANewerOne_ThatFinishedFirst()
    {
        using var service = Create();
        var olderHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ExecutionContextResolvedForTests = request =>
        {
            if (request.Connections?.Source.Contains("Stock", StringComparison.Ordinal) != true) return Task.CompletedTask;
            olderHeld.TrySetResult();
            return release.Task;
        };

        var older = service.UpdateExecutionContextAsync(Connection("Stock"));
        await olderHeld.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await service.UpdateExecutionContextAsync(Connection("Audit"));
        release.TrySetResult();
        await older.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.True(await OffersAsync(service, "Audit"));
        Assert.False(await OffersAsync(service, "Stock"));
    }

    // The slow part of a request runs without the workspace lock: the editor keeps completing, with
    // the context it had, while a large contract is still being looked up.
    [Fact]
    public async Task ASlowContextRequest_Should_NotBlockCompletion()
    {
        using var service = Create();
        await service.UpdateExecutionContextAsync(Connection("Stock"));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ExecutionContextResolvedForTests = _ =>
        {
            held.TrySetResult();
            return release.Task;
        };

        var slow = service.UpdateExecutionContextAsync(Connection("Audit"));
        try
        {
            await held.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            Assert.True(await OffersAsync(service, "Stock").WaitAsync(Patience, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
        }
        await slow.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(await OffersAsync(service, "Audit"));
    }

    // The schema is replaced while a completion has its snapshot: the list that is answered is the
    // newest solution's, never the replaced one's members.
    [Fact]
    public async Task ACompletionOvertakenByANewContext_Should_ListTheNewestMembers()
    {
        using var service = Create();
        await service.UpdateExecutionContextAsync(Connection("Stock"));
        var snapshotTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        service.CompletionSnapshotTakenForTests = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Task.CompletedTask;
            snapshotTaken.TrySetResult();
            return proceed.Task;
        };

        var completion = OffersAndAvoidsAsync(service, "Audit", "Stock");
        await snapshotTaken.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await service.UpdateExecutionContextAsync(Connection("Audit"));
        proceed.TrySetResult();

        Assert.True(await completion.WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    private static async Task<bool> OffersAndAvoidsAsync(CSharpIntelliSenseService service, string offered, string avoided)
    {
        const string code = "Connections.";
        var completions = await service.GetCompletionsAsync(code, code.Length, ct: TestContext.Current.CancellationToken);
        return completions is not null
            && completions.Items.Any(i => i.DisplayText == offered)
            && completions.Items.All(i => i.DisplayText != avoided);
    }

    // Typing continues, then the context changes, while the first pass is still waiting to analyse:
    // what reaches the editor describes the last text on the newest solution, nothing older.
    [Fact]
    public async Task RapidTyping_WithAContextChange_Should_PublishOnlyTheNewestState()
    {
        using var service = Create();
        const string first = "Connections.Stock.ToString();";
        const string last = "var x = 1; Connections.Stock.ToString();";
        var published = new ConcurrentQueue<IReadOnlyList<ISemanticToken>>();
        var current = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SemanticTokensChanged += (_, tokens) =>
        {
            published.Enqueue(tokens);
            if (tokens.Any(t => t is { StartOffset: 11, Length: 11, Kind: SemanticTokenKind.ClassName })) current.TrySetResult();
        };

        var snapshotTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        service.AnalysisSnapshotTakenForTests = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Task.CompletedTask;
            snapshotTaken.TrySetResult();
            return proceed.Task;
        };

        service.NotifyTextChanged(first);
        await snapshotTaken.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        foreach (var text in new[] { "var ", "var x = 1; Conn", last }) service.NotifyTextChanged(text);
        await service.UpdateExecutionContextAsync(Connection("Stock"));
        proceed.TrySetResult();

        await current.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(published, tokens => tokens.Any(t => t is { StartOffset: 0, Length: 11, Kind: SemanticTokenKind.ClassName }));
    }

    private static bool IsClassColoured(IReadOnlyList<ISemanticToken> tokens) =>
        tokens.Any(t => t is { StartOffset: 0, Length: 11, Kind: SemanticTokenKind.ClassName });
}
