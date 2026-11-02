using Invoklet.Connectors.IntegrationTests.Connectors.EfCore.Fixtures;
using Invoklet.Connectors.SqlServer;
using Invoklet.Languages.DotnetCommon.Sdk;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.SqlServer;

public sealed class SqlServerContextScriptTests
{
    [Fact]
    [Trait("Category", "RealBuild")]
    public async Task TheGeneratedContext_Should_CompileAgainstTheSqlServerProvider()
    {
        if (!new DotnetSdkProvider().Get().Any()) return;
        using var fixture = new ProviderCompileFixture();

        var output = await fixture.RunAsync(new SqlServerConnectionDriver(), "Server=localhost;Database=shop", ProviderCompileFixture.ModelScript);

        Assert.Equal(["Id", "Customer", "True"], output.TakeLast(3));
    }
}
