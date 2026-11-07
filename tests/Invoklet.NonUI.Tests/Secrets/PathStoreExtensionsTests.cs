using Invoklet.Secrets;
using Moq;
using Xunit;
using static Invoklet.NonUI.Tests.Secrets.PathStoreExtensionsFixture;

namespace Invoklet.NonUI.Tests.Secrets;

// Reading and saving one named path over whatever store holds them.
public sealed class PathStoreExtensionsTests
{
    [Fact]
    public void Read_ANameInAnotherCase_FindsThePath()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();
        fixture.Holds(new NamedPath("Data", At("data")));

        // Act
        var path = fixture.CreateSut().Read(" data ");

        // Assert
        Assert.Equal(At("data"), path);
    }

    [Fact]
    public void Read_ANameTheStoreHasNot_IsNull()
        => Assert.Null(new PathStoreExtensionsFixture().CreateSut().Read("data"));

    [Fact]
    public void Read_AnInvalidName_ThrowsWithoutReadingTheStore()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();

        // Act
        var thrown = Record.Exception(() => fixture.CreateSut().Read("my data"));

        // Assert
        Assert.IsType<ArgumentException>(thrown);
        fixture.Store.Verify(s => s.LoadSnapshot(), Times.Never);
    }

    [Fact]
    public void Put_ANewName_AddsItAfterTheOthers()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();
        fixture.Holds(new NamedPath("data", At("data")));

        // Act
        var saved = fixture.CreateSut().Put("tools", At("tools"));

        // Assert
        Assert.Equal(At("tools"), saved);
        Assert.Equal([new NamedPath("data", At("data")), new NamedPath("tools", At("tools"))], Assert.Single(fixture.Saved));
        fixture.Store.Verify(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), "v1"));
    }

    [Fact]
    public void Put_ANameInAnotherCase_ReplacesThePathAndKeepsTheName()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();
        fixture.Holds(new NamedPath("Data", At("old")), new NamedPath("tools", At("tools")));

        // Act
        fixture.CreateSut().Put("DATA", At("new"));

        // Assert
        Assert.Equal([new NamedPath("Data", At("new")), new NamedPath("tools", At("tools"))], Assert.Single(fixture.Saved));
    }

    [Fact]
    public void Put_Null_RemovesThePath()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();
        fixture.Holds(new NamedPath("data", At("data")), new NamedPath("tools", At("tools")));

        // Act
        var saved = fixture.CreateSut().Put("Data", null);

        // Assert
        Assert.Null(saved);
        Assert.Equal([new NamedPath("tools", At("tools"))], Assert.Single(fixture.Saved));
    }

    [Fact]
    public void Put_NullForANameTheStoreHasNot_SavesNothing()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();

        // Act
        fixture.CreateSut().Put("data", null);

        // Assert
        Assert.Empty(fixture.Saved);
    }

    [Fact]
    public void Put_AnInvalidPath_ThrowsWithoutSaving()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();

        // Act
        var thrown = Record.Exception(() => fixture.CreateSut().Put("data", "relative"));

        // Assert
        Assert.IsType<ArgumentException>(thrown);
        Assert.Empty(fixture.Saved);
    }

    [Fact]
    public void Put_AStoreAnotherWindowChanged_SavesAgainOnTheNewerOne()
    {
        // Arrange
        var fixture = new PathStoreExtensionsFixture();
        fixture.ChangesUnderTheFirstSave();

        // Act
        fixture.CreateSut().Put("data", At("data"));

        // Assert
        fixture.Store.Verify(s => s.LoadSnapshot(), Times.Exactly(2));
        fixture.Store.Verify(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>()), Times.Exactly(2));
    }
}
