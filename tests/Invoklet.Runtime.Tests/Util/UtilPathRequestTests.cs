using Xunit;
using static Invoklet.Runtime.Tests.UtilSecretRequestFixture;

namespace Invoklet.Runtime.Tests;

// A script Invoklet runs asks it for one named path and gets the path, none, why the store can't be read or why the name isn't valid.
// Console and environment are process-wide: see ConsoleTestCollection.
[Collection(ConsoleTestCollection.Name)]
public sealed class UtilPathRequestTests
{
    [Fact]
    public void GetPath_ASavedPath_ReturnsThePathInvokletAnswers()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(Found("/work/data"));

        // Act
        var path = Util.GetPath("data");

        // Assert
        Assert.Equal("/work/data", path);
    }

    [Fact]
    public void GetPath_AnyName_AsksInvokletToReadThatPath()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(Found("/x"));

        // Act
        Util.GetPath("data");

        // Assert
        using var request = fixture.FirstRequest();
        Assert.Equal("data", request.RootElement.GetProperty("Path").GetString());
        Assert.True(request.RootElement.GetProperty("Read").GetBoolean());
    }

    [Fact]
    public void GetPath_APathTheStoreHasNot_ThrowsSayingWhereToAddIt()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("0");

        // Act
        var thrown = Assert.Throws<KeyNotFoundException>(() => Util.GetPath("data"));

        // Assert
        Assert.Contains("'data'", thrown.Message);
        Assert.Contains("Tools → Path Store", thrown.Message);
    }

    [Fact]
    public void GetPath_AStoreInvokletCantRead_ThrowsWithItsReason()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(Unreadable("damaged"));

        // Act
        var thrown = Assert.Throws<InvalidDataException>(() => Util.GetPath("data"));

        // Assert
        Assert.Equal("damaged", thrown.Message);
    }

    [Fact]
    public void GetPath_ANameInvokletRejects_ThrowsWithItsReason()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(Invalid("bad name"));

        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Util.GetPath("my data"));

        // Assert
        Assert.StartsWith("bad name", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPath_AScriptNotStartedByInvoklet_ThrowsAndAsksNothing()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(Found("/x"));
        fixture.NotStartedByInvoklet();

        // Act
        var thrown = Record.Exception(() => Util.GetPath("data"));

        // Assert
        Assert.IsType<InvalidOperationException>(thrown);
    }
}
