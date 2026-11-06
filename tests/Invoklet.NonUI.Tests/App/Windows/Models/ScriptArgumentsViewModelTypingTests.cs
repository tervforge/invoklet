using Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Windows.Models;

// Typing a value into an argument set keeps its rows: rebuilt on each key, the field typed in was replaced and lost the focus.
[Trait("Platform", "Independent")]
public sealed class ScriptArgumentsViewModelTypingTests
{
    [Fact]
    public void Text_AValueTyped_KeepsTheSetsRows()
    {
        // Arrange
        var fixture = new ScriptArgumentsViewModelTypingFixture();
        var sut = fixture.CreateSut();
        var rows = sut.SelectedSet!.Values;

        // Act
        rows[0].Text = "34";

        // Assert
        Assert.Same(rows, sut.SelectedSet.Values);
    }

    [Fact]
    public void Text_AValueTyped_IsInTheDraft()
    {
        // Arrange
        var fixture = new ScriptArgumentsViewModelTypingFixture();
        var sut = fixture.CreateSut();
        var edited = 0;
        sut.Edited += () => edited++;

        // Act
        sut.SelectedSet!.Values[0].Text = "34";

        // Assert
        Assert.Equal(1, edited);
        Assert.True(sut.HasChanges);
    }

    [Fact]
    public void Name_ASetRenamed_KeepsItsRows()
    {
        // Arrange
        var fixture = new ScriptArgumentsViewModelTypingFixture();
        var sut = fixture.CreateSut();
        var rows = sut.SelectedSet!.Values;

        // Act
        sut.SelectedSet.Name = "smoke2";

        // Assert
        Assert.Same(rows, sut.SelectedSet.Values);
    }
}
