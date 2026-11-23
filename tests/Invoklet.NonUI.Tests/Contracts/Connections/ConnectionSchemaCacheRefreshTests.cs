using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// A Refresh that fails keeps the schema read before and says why; the next one that succeeds replaces it and clears that.
public sealed class ConnectionSchemaCacheRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_AReadUsingAnOlderSecret_CannotReplaceTheNewerSchemaOrError(bool oldReadFails)
    {
        using var fixture = new ConnectionSchemaCacheRefreshFixture();
        fixture.ResolveAddressFromSecret();
        fixture.SecretValue = "old";
        var old = fixture.HoldRead("old");
        var next = fixture.HoldRead("new");
        var sut = fixture.CreateSut();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var first = sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, timeout.Token);
        await old.Started.WaitAsync(timeout.Token);
        fixture.SecretValue = "new";
        var second = sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, timeout.Token);
        await next.Started.WaitAsync(timeout.Token);
        var loading = sut.Cache.IsFetching(fixture.Connection);
        next.Result.SetResult(new ConnectionSchema("new schema", [], ""));
        var updated = await second;
        if (oldReadFails) old.Result.SetException(new InvalidOperationException("stale failure"));
        else old.Result.SetResult(new ConnectionSchema("old schema", [], ""));
        var superseded = await first;

        Assert.True(loading);
        Assert.True(updated);
        Assert.False(superseded);
        Assert.False(sut.Cache.IsFetching(fixture.Connection));
        Assert.Equal("new schema", sut.Cache.Get(fixture.Connection)?.Summary);
        Assert.Null(sut.Cache.ErrorFor(fixture.Connection));
    }

    [Fact]
    public async Task EnsureAsync_TheSecretWasRemoved_DiscardsAnOlderRead()
    {
        using var fixture = new ConnectionSchemaCacheRefreshFixture();
        fixture.ResolveAddressFromSecret();
        fixture.SecretValue = "old";
        var old = fixture.HoldRead("old");
        var sut = fixture.CreateSut();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var first = sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, timeout.Token);
        await old.Started.WaitAsync(timeout.Token);

        fixture.SecretValue = null;
        await sut.EnsureAsync([fixture.Connection], null, ConnectionSchemaRefresh.All, timeout.Token);
        old.Result.SetResult(new ConnectionSchema("old schema", [], ""));
        var superseded = await first;

        Assert.False(superseded);
        Assert.False(sut.Cache.IsFetching(fixture.Connection));
        Assert.Null(sut.Cache.Get(fixture.Connection));
        Assert.Contains("address", sut.Cache.ErrorFor(fixture.Connection));
    }

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
