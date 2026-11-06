using Invoklet.NonUI.Tests.App.UserControls.Models.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.App.UserControls.Models;

// Debug mode needs a debugger: without one a run is a plain run and the bug button is gone; the choice comes back with one.
[Trait("Platform", "Independent")]
public sealed class EditorTabItemViewModelDebuggerTests
{
    [Fact]
    public void DebugModeEnabled_NoDebugger_IsOff()
    {
        // Arrange
        var fixture = new EditorTabItemViewModelDebuggerFixture { Installed = false };

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.False(sut.HasDebugger);
        Assert.False(sut.DebugModeEnabled);
    }

    [Fact]
    public void DebugModeEnabled_ADebugger_IsOnByDefault()
    {
        // Arrange
        var fixture = new EditorTabItemViewModelDebuggerFixture { Installed = true };

        // Act
        var sut = fixture.CreateSut();

        // Assert
        Assert.True(sut.HasDebugger);
        Assert.True(sut.DebugModeEnabled);
    }

    [Fact]
    public void HasDebugger_ADebuggerInstalledLater_BringsDebugModeBack()
    {
        // Arrange
        var fixture = new EditorTabItemViewModelDebuggerFixture { Installed = false };
        var sut = fixture.CreateSut();
        fixture.Installed = true;

        // Act
        fixture.RaisePreferencesChanged();

        // Assert
        Assert.True(sut.HasDebugger);
        Assert.True(sut.DebugModeEnabled);
    }
}
