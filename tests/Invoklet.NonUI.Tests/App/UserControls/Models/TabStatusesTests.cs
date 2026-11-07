using Invoklet.UserControls.Models;
using Xunit;

namespace Invoklet.NonUI.Tests.App.UserControls.Models;

[Trait("Platform", "Independent")]
public sealed class TabStatusesTests
{
    [Theory]
    [InlineData(false, RunState.Idle, false, TabStatus.None)]
    [InlineData(false, RunState.Idle, true, TabStatus.Unsaved)]
    [InlineData(false, RunState.Run, false, TabStatus.Running)]
    [InlineData(false, RunState.RunDebug, false, TabStatus.Debugging)]
    [InlineData(false, RunState.Paused, false, TabStatus.Paused)]
    [InlineData(true, RunState.Idle, false, TabStatus.Failed)]
    [InlineData(false, RunState.Run, true, TabStatus.Running)]
    [InlineData(false, RunState.Paused, true, TabStatus.Paused)]
    [InlineData(true, RunState.Run, true, TabStatus.Failed)]
    public void Of_StatesOfATab_PicksTheMostImportant(bool hadRunError, RunState runState, bool isDirty, TabStatus expected)
    {
        // Arrange (the states are the arguments)

        // Act
        var status = TabStatuses.Of(hadRunError, runState, isDirty);

        // Assert
        Assert.Equal(expected, status);
    }

    [Fact]
    public void Describe_NoStatus_HasNoText()
    {
        // Arrange (nothing to set up)

        // Act
        var text = TabStatuses.Describe(TabStatus.None);

        // Assert
        Assert.Null(text);
    }

    [Theory]
    [InlineData(TabStatus.Unsaved)]
    [InlineData(TabStatus.Running)]
    [InlineData(TabStatus.Debugging)]
    [InlineData(TabStatus.Paused)]
    [InlineData(TabStatus.Failed)]
    public void Describe_AStatus_HasTextOfItsOwn(TabStatus status)
    {
        // Arrange (the status is the argument)

        // Act
        var text = TabStatuses.Describe(status);

        // Assert
        Assert.False(string.IsNullOrEmpty(text));
    }
}
