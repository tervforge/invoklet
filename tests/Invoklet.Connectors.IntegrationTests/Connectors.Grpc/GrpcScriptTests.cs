using System.Text;
using Invoklet.Connectors.IntegrationTests.Connectors.Grpc.Fixtures;
using Invoklet.Contracts.Models;
using Invoklet.Languages.DotnetCommon;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Grpc;

// A script that calls a real gRPC server through its connection's generated client, and what the editor knows of that client.
public sealed class GrpcScriptTests(GrpcScriptFixture grpc) : IClassFixture<GrpcScriptFixture>
{
    private const string MessageType = "var request = new Inventory.V1.ItemRequest();";
    private static readonly int MessagePosition = MessageType.IndexOf("ItemRequest", StringComparison.Ordinal) + 2;

    private const string PartialScript = """
        var request = new Inventory.V1.ItemRequest { Sku = "extended" };
        Console.WriteLine(request.CustomValue);
        namespace Inventory.V1
        {
            public partial class ItemRequest { public string CustomValue => Sku; }
        }
        """;

    // The connection's metadata goes with each call, and the caller's own metadata goes too, unchanged after the calls.
    [Fact]
    public async Task RunAsync_AScriptCallingAService_GetsTheAnswerWithTheConnectionsMetadata()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        await using var host = grpc.CreateHost(grpc.NewOutputDirectory());

        // Act
        var (result, output, errors) = await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript);

        // Assert
        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.Equal("A1 42 Bearer t0k 1", output[^1]);
    }

    [Fact]
    public async Task RunAsync_UpdatedMetadata_IsSentToTheServerWithoutRecompiling()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        var directory = grpc.NewOutputDirectory();
        const string first = "Bearer grpc-startup-first";
        const string second = "Bearer grpc-startup-second";

        await using var initial = grpc.CreateHost(directory, first);
        var initialCompiled = false;
        initial.StageChanged += (stage, _) => { if (stage == RunStage.Compiling) initialCompiled = true; };
        var (firstResult, firstOutput, firstErrors) = await GrpcScriptFixture.RunAsync(initial, GrpcScriptFixture.CallingScript);
        await using var updated = grpc.CreateHost(directory, second);
        var updatedCompiled = false;
        updated.StageChanged += (stage, _) => { if (stage == RunStage.Compiling) updatedCompiled = true; };
        var (secondResult, secondOutput, secondErrors) = await GrpcScriptFixture.RunAsync(updated, GrpcScriptFixture.CallingScript);

        Assert.True(firstResult.Succeeded, string.Join('\n', firstErrors));
        Assert.True(secondResult.Succeeded, string.Join('\n', secondErrors));
        Assert.Equal($"A1 42 {first} 1", firstOutput[^1]);
        Assert.Equal($"A1 42 {second} 1", secondOutput[^1]);
        Assert.True(initialCompiled);
        Assert.False(updatedCompiled);
        var dll = Directory.EnumerateFiles(directory, "InvokletQuery.dll", SearchOption.AllDirectories).Single();
        Assert.Equal(-1, File.ReadAllBytes(dll).AsSpan().IndexOf(Encoding.Unicode.GetBytes("grpc-startup-")));
    }

    // Neither CS8669 nor any other nullable warning from the generated classes or the wrapper.
    [Fact]
    public async Task RunAsync_TheConnectionsGeneratedCode_BuildsWithoutNullableWarnings()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        await using var host = grpc.CreateHost(grpc.NewOutputDirectory());

        // Act
        var (_, output, errors) = await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript);

        // Assert
        Assert.DoesNotContain(output.Concat(errors), line => line.Contains("CS86", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AnEditedScript_KeepsTheBuiltContract()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        var outputDirectory = grpc.NewOutputDirectory();
        await using var host = grpc.CreateHost(outputDirectory);
        await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript);
        var contract = Directory.GetFiles(Path.Combine(outputDirectory, "obj", "connection-contracts"), "*.dll").Single();
        var built = File.GetLastWriteTimeUtc(contract);

        // Act
        var (result, _, errors) = await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript + "\n// edited");

        // Assert
        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.Equal(built, File.GetLastWriteTimeUtc(contract));
    }

    // The contract the script loads, deleted from bin, is copied there again; the script itself isn't built again.
    [Fact]
    public async Task RunAsync_TheRuntimeContractDeleted_IsCopiedAgainWithoutABuild()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        var outputDirectory = grpc.NewOutputDirectory();
        await using var host = grpc.CreateHost(outputDirectory);
        await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript);
        var contract = Directory.GetFiles(Path.Combine(outputDirectory, "obj", "connection-contracts"), "*.dll").Single();
        var runtimeContract = Path.Combine(outputDirectory, "bin", Path.GetFileName(contract));
        var binary = Path.Combine(outputDirectory, "bin", "InvokletQuery.dll");
        var built = File.GetLastWriteTimeUtc(binary);
        File.Delete(runtimeContract);

        // Act
        var (result, _, errors) = await GrpcScriptFixture.RunAsync(host, GrpcScriptFixture.CallingScript);

        // Assert
        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.True(File.Exists(runtimeContract));
        Assert.Equal(built, File.GetLastWriteTimeUtc(binary));
    }

    [Fact]
    public async Task RunAsync_AScriptExtendingAMessage_UsesItsPartialClass()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        await using var host = grpc.CreateHost(grpc.NewOutputDirectory());

        // Act
        var (result, output, errors) = await GrpcScriptFixture.RunAsync(host, PartialScript);

        // Assert
        Assert.True(result.Succeeded, string.Join('\n', errors));
        Assert.Equal("extended", output[^1]);
    }

    // Before any build: the services come from the generated document.
    [Fact]
    public async Task GetCompletionsAsync_BeforeABuild_OffersTheServices()
    {
        // Arrange
        using var editor = await grpc.EditorAsync();
        const string code = "Connections.Inventory.";

        // Act
        var members = await editor.GetCompletionsAsync(code, code.Length, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(members!.Items, i => i.DisplayText == "Stock");
        Assert.Contains(members.Items, i => i.DisplayText == "Audit");
    }

    // F12 on a symbol only in the generated document, with no contract built yet to decompile, opens that document at the declaration.
    [Fact]
    public async Task GetDefinitionAsync_ASymbolOnlyInTheGeneratedDocument_OpensItAtTheDeclaration()
    {
        // Arrange
        using var editor = await grpc.EditorAsync();
        const string code = "Connections.Inventory.Stock";

        // Act
        var definition = await editor.GetDefinitionAsync(code, code.Length - 1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(definition);
        Assert.False(definition.IsInSource);
        var (text, line) = definition.GeneratedDocument!.Value;
        Assert.Contains(" Stock ", text.Split('\n')[line - 1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCompletionsAsync_AfterABuild_OffersTheClientsMethods()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        using var editor = await grpc.EditorAsync(grpc.BuiltDirectory);
        const string code = "Connections.Inventory.Stock.";

        // Act
        var members = await editor.GetCompletionsAsync(code, code.Length, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(members!.Items, i => i.DisplayText == "GetItemAsync");
    }

    [Fact]
    public async Task GetDefinitionAsync_AMessageAfterABuild_IsInTheContract()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        using var editor = await grpc.EditorAsync(grpc.BuiltDirectory);

        // Act
        var definition = await editor.GetDefinitionAsync(MessageType, MessagePosition, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(grpc.Contract, definition!.AssemblyPath);
    }

    [Fact]
    public async Task Decompile_AMessageOfTheContract_ShowsItsClass()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        using var editor = await grpc.EditorAsync(grpc.BuiltDirectory);
        var definition = await editor.GetDefinitionAsync(MessageType, MessagePosition, TestContext.Current.CancellationToken);

        // Act
        var decompiled = CSharpDecompilerService.Decompile(definition!);

        // Assert
        Assert.Contains("class ItemRequest", decompiled.Text, StringComparison.Ordinal);
        Assert.Contains("Sku", decompiled.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDefinitionAsync_APartialClassInTheScript_IsInTheSource()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        using var editor = await grpc.EditorAsync(grpc.BuiltDirectory);

        // Act
        var definition = await editor.GetDefinitionAsync(PartialScript, PartialScript.IndexOf("CustomValue", StringComparison.Ordinal) + 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(definition?.IsInSource);
    }

    [Fact]
    public async Task GetDefinitionAsync_WithoutTheConnection_FindsNoMessage()
    {
        Assert.SkipWhen(grpc.Sdk is null, "No .NET SDK is installed.");
        // Arrange
        using var editor = await grpc.EditorAsync(grpc.BuiltDirectory, withConnection: false);

        // Act
        var definition = await editor.GetDefinitionAsync(MessageType, MessagePosition, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(definition);
    }
}
