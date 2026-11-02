using Invoklet.Connectors.IntegrationTests.Connectors.EfCore.Fixtures;
using Invoklet.Connectors.Postgres;
using Invoklet.Languages.DotnetCommon.Sdk;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Postgres;

public sealed class PostgresContextScriptTests
{
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
