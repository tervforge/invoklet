using System.Text;
using Invoklet.Connectors.IntegrationTests.Connectors.EfCore.Fixtures;
using Invoklet.Connectors.Postgres;
using Invoklet.Languages.DotnetCommon.Sdk;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Postgres;

public sealed class PostgresContextScriptTests
{
    [Fact]
    [Trait("Category", "RealBuild")]
    public async Task AChangedConnectionString_Should_ReachTheNextRunWithoutEnteringTheCompiledScript()
    {
        Assert.SkipUnless(new DotnetSdkProvider().Get().Any(), "A .NET SDK is required to run the script.");
        using var fixture = new ProviderCompileFixture();
        var driver = new PostgresConnectionDriver();
        const string first = "Host=localhost;Database=shop;Password=startup-test-first";
        const string second = "Host=localhost;Database=shop;Password=startup-test-second";
        const string script = "Console.WriteLine(Connections.Shop.ConnectionString);";

        var initialOutput = await fixture.RunAsync(driver, first, script);
        var initialCompiled = fixture.LastRunCompiled;
        var initialDll = fixture.ReadCompiledScript();
        var updatedOutput = await fixture.RunAsync(driver, second, script);
        var updatedDll = fixture.ReadCompiledScript();

        Assert.Equal(first, Assert.Single(initialOutput));
        Assert.Equal(second, Assert.Single(updatedOutput));
        Assert.True(initialCompiled);
        Assert.False(fixture.LastRunCompiled);
        Assert.Equal(initialDll, updatedDll);
        Assert.Equal(-1, updatedDll.AsSpan().IndexOf(Encoding.Unicode.GetBytes("startup-test")));
    }

    [Fact]
    [Trait("Category", "RealBuild")]
    public async Task TheGeneratedContext_Should_CompileAgainstTheNpgsqlProvider()
    {
        if (!new DotnetSdkProvider().Get().Any()) return;
        using var fixture = new ProviderCompileFixture();

        var output = await fixture.RunAsync(new PostgresConnectionDriver(), "Host=localhost;Database=shop", ProviderCompileFixture.ModelScript);

        Assert.Equal(["Id", "Customer", "True"], output.TakeLast(3));
    }
}
