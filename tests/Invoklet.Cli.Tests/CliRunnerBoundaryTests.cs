using Invoklet.Contracts;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.DotnetCommon.Sdk;
using Spectre.Console.Testing;
using Xunit;

namespace Invoklet.Cli.Tests;

// The command has one boundary: Ctrl+C is 130 in whatever phase it comes, and a file that cannot be read or written is a message.
public sealed class CliRunnerBoundaryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-cli-boundary").FullName;

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            foreach (var file in Directory.EnumerateFiles(_folder)) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.Delete(_folder, true);
    }

    private string Script(string front = "language: csharp\n")
    {
        var path = Path.Combine(_folder, "a.inv");
        File.WriteAllText(path, $"---\n{front}---\n\n```csharp run\n1.Dump();\n```\n");
        return path;
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeTheScriptIsRead_IsExit130WithNoException()
    {
        // Arrange
        var path = Script();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var exitCode = await CliRunner.RunAsync([path], new CliServices(new LanguageHostRegistry(), new FakeSdks()), new TestConsole(), new TestConsole(), cancelled.Token);

        // Assert
        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileAConnectionsDescriptionIsRead_IsExit130WithNoException()
    {
        // Arrange
        var path = Script("language: csharp\nconnections:\n  - name: stock\n    driver: blocking\n    params:\n      address: http://localhost:5000\n");
        var driver = new BlockingSchemaDriver();
        var drivers = new ConnectionDriverRegistry();
        drivers.Register(driver);
        var languages = new LanguageHostRegistry();
        languages.Register(new NoLanguage());
        var schemas = new ConnectionSchemas(drivers, Path.Combine(_folder, "schemas"),
            new ConnectionTools { GetPackageFolderAsync = (_, _, _) => Task.FromResult("") }, _ => null);
        var services = new CliServices(languages, new FakeSdks(), drivers, schemas);
        using var cancel = new CancellationTokenSource();

        // Act
        var run = CliRunner.RunAsync([path], services, new TestConsole(), new TestConsole(), cancel.Token);
        await driver.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task RunAsync_ACsharpScriptThatCannotBeRead_IsAMessageAndExit2()
    {
        // Arrange
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = Script();
        File.SetUnixFileMode(path, UnixFileMode.None);
        var stderr = new TestConsole();

        // Act
        var exitCode = await CliRunner.RunAsync([path], new CliServices(new LanguageHostRegistry(), new FakeSdks()), new TestConsole(), stderr, CancellationToken.None);

        // Assert
        Assert.Equal(CliRunner.NotRunExitCode, exitCode);
        Assert.StartsWith("error:", stderr.Output.TrimStart());
    }

    [Fact]
    public async Task Convert_TheOutputCannotBeWritten_IsAMessageAndExit1()
    {
        // Arrange
        var linq = Path.Combine(_folder, "q.linq");
        File.WriteAllText(linq, "<Query Kind=\"Statements\">\n</Query>\n\nConsole.WriteLine(\"hi\");\n");
        var stderr = new TestConsole();

        // Act
        var exitCode = await CliRunner.RunAsync(["convert", linq, Path.Combine(_folder, "no-such-folder", "q.inv")], new TestConsole(), stderr, CancellationToken.None);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.StartsWith("error:", stderr.Output.TrimStart());
    }

    // A language that is there to be found; the run is stopped before it is asked for anything.
    private sealed class NoLanguage : ILanguageHostFactory
    {
        public string Language => "csharp";
        public string DisplayName => "C#";
        public LanguageHostCapabilities Capabilities => default;
        public IReadOnlyDictionary<string, string> Snippets => new Dictionary<string, string>();
        public EditorLanguageProfile EditorProfile => new();
        public ILanguageHost Create(ILanguageHostOptions? options = null) => throw new InvalidOperationException("not reached");
        public ILanguageHost Create(ExecutionRequest request) => throw new InvalidOperationException("not reached");
    }

    private sealed class FakeSdks : ISdkProvider
    {
        public IReadOnlyList<DotnetSdk> Get(string? dotnetExecutableOverride = null) => [new("net10.0", "10.0.100", ".NET 10.0.100", ".NET 10")];
    }

    // A description that is read until the run is cancelled.
    private sealed class BlockingSchemaDriver : IConnectionSchemaDriver, ICSharpClientSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Id => "blocking";
        public string DisplayName => "Blocking";
        public ConnectionCategory Category => ConnectionCategory.Grpc;
        public IReadOnlyList<ConnectionParam> Params => [];
        public IReadOnlyDictionary<string, string> Packages => new Dictionary<string, string>();
        public IReadOnlyList<string> Validate(MdRunConnection connection) => [];
        public Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectionTestResult(true, ""));
        public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context) => new("");
        public async Task<ConnectionSchema> FetchSchemaAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, ConnectionTools tools,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ConnectionSchema("never", [], "");
        }
    }
}
