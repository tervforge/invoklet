using Avalonia.Headless.XUnit;
using Invoklet.Avalonia.CodeTextEditor.Breakpoints;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// The breakpoints of the gutter and of the running debug session stay the same, and the debugger's answers colour the gutter.
public sealed class EditorBreakpointsTests
{
    [AvaloniaFact]
    public void OnAdded_WhileRunning_SendsTheBreakpointToTheSession()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture { Running = true };
        fixture.CreateSut();

        // Act
        fixture.Editor.AddBreakpoint(2);

        // Assert
        var sent = Assert.Single(fixture.Added);
        Assert.Equal((EditorBreakpointsFixture.SourceFile, 2), (sent.FileName, sent.Line));
    }

    [AvaloniaFact]
    public void OnAdded_WithoutASession_SendsNothing()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture();
        fixture.CreateSut();

        // Act
        fixture.Editor.AddBreakpoint(2);

        // Assert
        Assert.Empty(fixture.Added);
    }

    [AvaloniaFact]
    public void OnRemoved_WhileRunning_TakesTheBreakpointFromTheSession()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture();
        fixture.CreateSut();
        fixture.Editor.AddBreakpoint(3);
        fixture.Running = true;

        // Act
        fixture.Editor.RemoveBreakpoint(fixture.At(3));

        // Assert
        Assert.Equal([3], fixture.Removed);
    }

    [AvaloniaFact]
    public void Apply_ANewBreakpointWhileRunning_SendsItOnceWithItsOptions()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture { Running = true };
        var sut = fixture.CreateSut();

        // Act
        sut.Apply(2, " count > 5 ", "% 2", enabled: true);

        // Assert
        var sent = Assert.Single(fixture.Added);
        Assert.Equal(("count > 5", "% 2"), (sent.Condition, sent.HitCondition));
        Assert.Equal("count > 5", fixture.At(2).Condition);
    }

    [AvaloniaFact]
    public void Apply_DisabledWhileRunning_TakesItFromTheSessionWithoutSendingItAgain()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture();
        var sut = fixture.CreateSut();
        fixture.Editor.AddBreakpoint(2);
        fixture.Running = true;

        // Act
        sut.Apply(2, "  ", null, enabled: false);

        // Assert
        Assert.Equal([2], fixture.Removed);
        Assert.Empty(fixture.Added);
        Assert.False(fixture.At(2).IsEnabled);
        Assert.Null(fixture.At(2).Condition);
    }

    [AvaloniaFact]
    public void Verified_TheDebuggersAnswer_ColoursTheBreakpointOfThatLine()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture();
        var sut = fixture.CreateSut();
        fixture.Editor.AddBreakpoint(2);

        // Act
        sut.Verified(EditorBreakpointsFixture.Reported(2), verified: false, "no code here");

        // Assert
        Assert.Equal(BreakpointVerificationState.Unverified, fixture.At(2).VerificationState);
        Assert.Equal("no code here", fixture.At(2).VerificationMessage);
    }

    [AvaloniaFact]
    public void StartRun_AfterAnEarlierRun_MakesEveryBreakpointPendingAndReturnsTheEnabledOnes()
    {
        // Arrange
        using var fixture = new EditorBreakpointsFixture();
        var sut = fixture.CreateSut();
        fixture.Editor.AddBreakpoint(1);
        fixture.Editor.AddBreakpoint(3);
        sut.Verified(EditorBreakpointsFixture.Reported(1), verified: true, null);
        sut.Apply(3, "x > 1", null, enabled: false);

        // Act
        var enabled = sut.StartRun();

        // Assert
        Assert.Equal([(1, (string?)null, (string?)null)], enabled);
        Assert.All(fixture.Editor.Breakpoints, b => Assert.Equal(BreakpointVerificationState.Pending, b.VerificationState));
    }
}
