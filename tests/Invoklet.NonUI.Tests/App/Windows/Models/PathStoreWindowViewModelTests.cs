using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Invoklet.Secrets;
using Moq;
using Xunit;
using static Invoklet.NonUI.Tests.App.Windows.Models.Fixtures.PathStoreWindowViewModelFixture;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// Tools → Path Store: named paths edited in the window and written whole on Save, by the store's own rules.
public sealed class PathStoreWindowViewModelTests
{
    [Fact]
    public void Constructor_AStoreWithPaths_ListsThemAndSelectsTheFirst()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")), new NamedPath("tools", At("tools")));

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.Equal(["data", "tools"], sut.Paths.Select(p => p.Name));
        Assert.Same(sut.Paths[0], sut.SelectedPath);
        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void Constructor_APathWithNothingThere_IsMarkedMissing()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")), new NamedPath("tools", At("tools")));
        fixture.Existing.Add(At("tools"));

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.Equal([true, false], sut.Paths.Select(p => p.IsMissing));
    }

    [Fact]
    public void Constructor_ADamagedStore_ReportsItAndCantSave()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.IsDamaged();

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.Contains("damaged", sut.StoreError);
        Assert.False(sut.CanSave);
        Assert.False(sut.Save());
        fixture.Store.Verify(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void AddCommand_TwiceInARow_GivesEachAName()
    {
        // Arrange
        var sut = new PathStoreWindowViewModelFixture().CreateSut();

        // Act
        sut.AddCommand.Execute().Subscribe();
        sut.AddCommand.Execute().Subscribe();

        // Assert
        Assert.Equal(["path", "path2"], sut.Paths.Select(p => p.Name));
        Assert.Same(sut.Paths[1], sut.SelectedPath);
        Assert.True(sut.HasChanges);
    }

    [Theory]
    [InlineData("my data", "/x")]
    [InlineData("data", "relative")]
    [InlineData("data", "")]
    public void Name_OrPathTheStoreWouldReject_CantBeSaved(string name, string path)
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")));
        var sut = fixture.CreateSut();

        // Act
        sut.Paths[0].Name = name;
        sut.Paths[0].Path = path == "/x" ? At("x") : path;

        // Assert
        Assert.NotNull(sut.ValidationError);
        Assert.False(sut.Save());
        Assert.Empty(fixture.Saved);
    }

    [Fact]
    public void Name_SameAsAnotherInAnotherCase_CantBeSaved()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")), new NamedPath("tools", At("tools")));
        var sut = fixture.CreateSut();

        // Act
        sut.Paths[1].Name = "DATA";

        // Assert
        Assert.Contains("'DATA'", sut.ValidationError);
    }

    [Fact]
    public void Save_APathUnderTheHomeFolder_WritesItInFullAndShowsIt()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        var sut = fixture.CreateSut();
        sut.AddCommand.Execute().Subscribe();
        sut.Paths[0].Name = " work ";
        sut.Paths[0].Path = "~/work";
        var full = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "work");

        // Act
        var saved = sut.Save();

        // Assert
        Assert.True(saved);
        Assert.Equal([new NamedPath("work", full)], Assert.Single(fixture.Saved));
        Assert.Equal(full, sut.Paths[0].Path);
        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void Save_AStoreChangedInAnotherWindow_SaysSoAndKeepsTheEdits()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")));
        fixture.ChangesInAnotherWindow();
        var sut = fixture.CreateSut();
        sut.Paths[0].Path = At("other");

        // Act
        var saved = sut.Save();

        // Assert
        Assert.False(saved);
        Assert.Contains("another window", sut.StoreError);
        Assert.True(sut.HasChanges);
    }

    [Fact]
    public void RemoveCommand_TheSelectedPath_SelectsTheNextAndCanBeSaved()
    {
        // Arrange
        var fixture = new PathStoreWindowViewModelFixture();
        fixture.Holds(new NamedPath("data", At("data")), new NamedPath("tools", At("tools")));
        var sut = fixture.CreateSut();

        // Act
        sut.RemoveCommand.Execute().Subscribe();
        sut.Save();

        // Assert
        Assert.Equal("tools", sut.SelectedPath?.Name);
        Assert.Equal([new NamedPath("tools", At("tools"))], Assert.Single(fixture.Saved));
        fixture.Store.Verify(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), "v1"));
    }
}
