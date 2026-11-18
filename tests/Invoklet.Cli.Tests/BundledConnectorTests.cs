using Xunit;

namespace Invoklet.Cli.Tests;

// Compiled only when the build bundles the connector packages (InvokletShipConnectors), the layout an installation has.
public class BundledConnectorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void CreateDefault_Should_LoadConnectorPluginsInTheExistingDriverOrder()
    {
        var services = CliServices.CreateDefault();

        Assert.Equal(["csharp", "vb"], services.Registry.SupportedLanguages);
        Assert.True(services.Plugins.IsLoaded("invoklet.connector.efcore"));
        Assert.True(services.Plugins.IsLoaded("invoklet.connector.http"));
        Assert.Equal(["http", "openapi", "grpc", "sqlite", "postgres", "sqlserver"],
            services.ConnectionDrivers!.Drivers.Select(driver => driver.Id));
    }
}
