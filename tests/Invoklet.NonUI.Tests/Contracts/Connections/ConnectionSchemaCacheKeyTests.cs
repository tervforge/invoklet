using Invoklet.Contracts.Connections;
using Invoklet.NonUI.Tests.Contracts.Connections.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Contracts.Connections;

// A driver's schema version is part of a description's key: descriptions written before the driver changed them are not used, and are read again.
[Trait("Platform", "Independent")]
public sealed class ConnectionSchemaCacheKeyTests
{
    [Fact]
    public void KeyOf_AnotherSchemaVersionOfTheDriver_IsAnotherKey()
    {
        // Arrange
        var fixture = new ConnectionSchemaCacheKeyFixture();
        var before = fixture.CreateSut("2");
        var after = new ConnectionSchemaCacheKeyFixture().CreateSut("3");

        // Act
        var oldKey = before.KeyOf(ConnectionSchemaCacheKeyFixture.Connection());
        var newKey = after.KeyOf(ConnectionSchemaCacheKeyFixture.Connection());

        // Assert
        Assert.NotEqual(oldKey, newKey);
    }

    [Fact]
    public void KeyOf_TheSameSchemaVersion_IsTheSameKey()
    {
        // Arrange
        var first = new ConnectionSchemaCacheKeyFixture().CreateSut("3");
        var second = new ConnectionSchemaCacheKeyFixture().CreateSut("3");

        // Act
        var firstKey = first.KeyOf(ConnectionSchemaCacheKeyFixture.Connection());
        var secondKey = second.KeyOf(ConnectionSchemaCacheKeyFixture.Connection());

        // Assert
        Assert.Equal(firstKey, secondKey);
    }
}
