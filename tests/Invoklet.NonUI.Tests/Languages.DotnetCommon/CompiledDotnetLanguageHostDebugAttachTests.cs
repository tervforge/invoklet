using Invoklet.NonUI.Tests.Languages.DotnetCommon.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon;

// A debugged script is started by Invoklet, with its own stdin (so it can ask for input), held by Invoklet.Runtime's startup hook; the
// debugger attaches to it by id, and once it is configured the script gets the line that lets it go on. The run ends with the script.
[Trait("Category", "Integration")]
public sealed class CompiledDotnetLanguageHostDebugAttachTests
{
    [Fact]
    public async Task RunDebugAsync_TheDebuggerInitialized_AttachesItToTheScriptsProcess()
    {
        // Arrange
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture();
        var sut = fixture.CreateSut();

        // Act
        _ = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);
        fixture.DebuggerInitialized();
        var sent = await fixture.SentToDebuggerAsync("\"command\":\"attach\"", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("\"command\":\"attach\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"processId\":4242", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDebugAsync_TheScriptStarted_IsGivenInvokletsStartupHook()
    {
        // Arrange
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture();
        var sut = fixture.CreateSut();

        // Act
        _ = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);
        fixture.DebuggerInitialized();
        await fixture.SentToDebuggerAsync("\"command\":\"attach\"", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(fixture.StartupHook, fixture.ScriptOptions?.StartupHook);
    }

    [Fact]
    public async Task RunDebugAsync_TheDebuggerNotConfiguredYet_HoldsTheScript()
    {
        // Arrange
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture();
        var sut = fixture.CreateSut();

        // Act
        _ = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);
        fixture.DebuggerInitialized();
        await fixture.SentToDebuggerAsync("\"command\":\"attach\"", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, fixture.Script.Input.Length);
    }

    [Fact]
    public async Task RunDebugAsync_TheDebuggerConfigured_LetsTheScriptGoOn()
    {
        // Arrange
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture();
        var sut = fixture.CreateSut();
        _ = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);
        fixture.DebuggerInitialized();
        await fixture.SentToDebuggerAsync("\"command\":\"attach\"", TestContext.Current.CancellationToken);

        // Act
        fixture.DebuggerConfigured();
        var sent = await fixture.SentToScriptAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("\n", sent);
    }

    [Fact]
    public async Task RunDebugAsync_ConnectionSettings_ArriveWithTheReleaseLine()
    {
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture
        {
            ConnectionStartupValues = ["Host=localhost;Password=debug-test"],
        };
        await using var sut = fixture.CreateSut();
        var ct = TestContext.Current.CancellationToken;
        var run = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, ct);
        fixture.DebuggerInitialized();
        await fixture.SentToDebuggerAsync("\"command\":\"attach\"", ct);
        Assert.Equal(0, fixture.Script.Input.Length);

        fixture.DebuggerConfigured();
        var sent = await fixture.SentToScriptAsync(ct);
        fixture.Script.Exit.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fixture.ConnectionStartupValues[0]));
        Assert.Equal($"invoklet-connections:{encoded}\n", sent);
    }

    [Fact]
    public async Task RunDebugAsync_TheScriptEnds_EndsTheRun()
    {
        // Arrange
        using var fixture = new CompiledDotnetLanguageHostDebugAttachFixture();
        var sut = fixture.CreateSut();
        var run = sut.RunDebugAsync("int x = 1;", outputHandler: static (_, _) => { }, TestContext.Current.CancellationToken);
        fixture.DebuggerInitialized();
        fixture.DebuggerConfigured();
        await fixture.SentToScriptAsync(TestContext.Current.CancellationToken);

        // Act
        fixture.Script.Exit.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(run.IsCompletedSuccessfully);
    }
}
