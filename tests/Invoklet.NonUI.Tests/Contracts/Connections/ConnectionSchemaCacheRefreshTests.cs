using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// A Refresh that fails keeps the schema read before and says why; the next one that succeeds replaces it and clears that.
public sealed class ConnectionSchemaCacheRefreshTests
{
    [Fact]
    public async Task EnsureAsync_ARefreshThatFails_KeepsThePreviousSchemaAndSaysWhy()
    {
        // Arrange
        using var fixture = new ConnectionSchemaCacheRefreshFixture();
        var sut = fixture.CreateSut();
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, TestContext.Current.CancellationToken);
        fixture.ServerFails("the server is down");

        // Act
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1 service", sut.Cache.Get(fixture.Connection)!.Summary);
        Assert.Equal("the server is down", sut.Cache.ErrorFor(fixture.Connection));
    }

    [Fact]
    public async Task EnsureAsync_ARefreshThatSucceedsAfterAFailure_ReplacesTheSchemaAndClearsTheError()
    {
        // Arrange
        using var fixture = new ConnectionSchemaCacheRefreshFixture();
        var sut = fixture.CreateSut();
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, TestContext.Current.CancellationToken);
        fixture.ServerFails("the server is down");
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, TestContext.Current.CancellationToken);
        fixture.ServerDescribes("2 services");

        // Act
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("2 services", sut.Cache.Get(fixture.Connection)!.Summary);
        Assert.Null(sut.Cache.ErrorFor(fixture.Connection));
    }
}
