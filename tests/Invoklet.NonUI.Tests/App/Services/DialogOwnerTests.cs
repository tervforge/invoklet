using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

[Trait("Platform", "Independent")]
public sealed class DialogOwnerTests
{
    private const string Main = "main";

    private static DialogOwner.Candidate<string> Window(string name, bool active, bool visible = true) => new(name, active, visible);

    [Fact]
    public void Choose_AnActiveShownWindow_OwnsTheDialog()
    {
        // Arrange
        DialogOwner.Candidate<string>[] windows = [Window(Main, false), Window("tab", true)];

        // Act
        var owner = DialogOwner.Choose(windows, Main);

        // Assert
        Assert.Equal("tab", owner);
    }

    [Fact]
    public void Choose_SeveralActive_TakesTheLastOpened()
    {
        // Arrange: a dialog opened from another one
        DialogOwner.Candidate<string>[] windows = [Window(Main, true), Window("preferences", true)];

        // Act
        var owner = DialogOwner.Choose(windows, Main);

        // Assert
        Assert.Equal("preferences", owner);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Choose_AWindowInactiveOrHidden_LeavesTheDialogToTheMainWindow(bool active, bool visible)
    {
        // Arrange
        DialogOwner.Candidate<string>[] windows = [Window(Main, false), Window("tab", active, visible)];

        // Act
        var owner = DialogOwner.Choose(windows, Main);

        // Assert
        Assert.Equal(Main, owner);
    }

    [Fact]
    public void Choose_NoWindowAndNoMain_IsNull()
    {
        // Arrange
        DialogOwner.Candidate<string>[] windows = [];

        // Act
        var owner = DialogOwner.Choose(windows, (string?)null);

        // Assert
        Assert.Null(owner);
    }
}
