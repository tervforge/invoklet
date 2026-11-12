using Invoklet.Contracts.Connections;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

[Trait("Platform", "Independent")]
public class ConnectionDriverRegistryTests
{
    private static IConnectionDriver Driver(string id)
    {
        var driver = new Mock<IConnectionDriver>();
        driver.SetupGet(d => d.Id).Returns(id);
        return driver.Object;
    }

    [Fact]
    public void Register_Should_RefuseASecondDriverOfAnId_LetterCaseAside()
    {
        var registry = new ConnectionDriverRegistry();
        var first = Driver("http");
        registry.Register(first);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(Driver("HTTP")));

        Assert.Contains("HTTP", ex.Message);
        Assert.Same(first, Assert.Single(registry.Drivers));
        Assert.Same(first, registry.Find("Http"));
    }

    [Fact]
    public void Register_Should_KeepDriversInRegistrationOrder()
    {
        var registry = new ConnectionDriverRegistry();
        registry.Register(Driver("sqlite"));
        registry.Register(Driver("http"));

        Assert.Equal(["sqlite", "http"], registry.Drivers.Select(d => d.Id));
    }
}
