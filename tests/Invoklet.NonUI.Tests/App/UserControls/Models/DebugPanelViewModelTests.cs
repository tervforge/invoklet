using Invoklet.Contracts.Debugging;
using Invoklet.Contracts.Models;
using Invoklet.UserControls.Models;
using Moq;
using Xunit;

namespace Invoklet.NonUI.Tests.App.UserControls.Models;

// A stop of the debugger fills the panel: threads, locals whose members load when expanded, and the call stack.
[Trait("Platform", "Independent")]
public sealed class DebugPanelViewModelTests
{
    private static IVariableInfo Variable(string name, string value, int reference = 0) =>
        Mock.Of<IVariableInfo>(v => v.Name == name && v.Value == value && v.TypeName == "int"
            && v.HasChildren == (reference != 0) && v.VariablesReference == reference);

    private static Mock<IBreakpointHitContext> Stop(params IVariableInfo[] locals)
    {
        var context = new Mock<IBreakpointHitContext>();
        context.SetupGet(c => c.LocalVariables).Returns(locals);
        context.SetupGet(c => c.Threads).Returns([Mock.Of<IThreadInfo>(t => t.Id == 7 && t.Name == "Main")]);
        context.SetupGet(c => c.CallStack).Returns([Mock.Of<IStackFrame>(f => f.MethodName == "Main" && f.FileName == "Script.cs" && f.Line == 4)]);
        context.SetupGet(c => c.StopReason).Returns(DebugStopReason.Breakpoint);
        return context;
    }

    [Fact]
    public void ShowHit_AStop_ReplacesWhatTheLastStopShowed()
    {
        // Arrange
        var sut = new DebugPanelViewModel();
        sut.ShowHit(Stop(Variable("old", "0")).Object);

        // Act
        sut.ShowHit(Stop(Variable("count", "3")).Object);

        // Assert
        var local = Assert.Single(sut.DebugLocals);
        Assert.Equal(("count", "3"), (local.Name, local.Value));
        Assert.Equal([new DebugThread(7, "Main")], sut.DebugThreads);
        Assert.Equal([new DebugFrame("Main", "Script.cs", 4)], sut.DebugCallStack);
        Assert.Null(sut.StoppedException);
    }

    [Fact]
    public void ShowHit_ALocalWithMembers_LoadsThemWhenExpanded()
    {
        // Arrange
        var context = Stop(Variable("person", "{Person}", reference: 5));
        context.Setup(c => c.GetChildrenAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Variable("Age", "31")]);
        var sut = new DebugPanelViewModel();
        sut.ShowHit(context.Object);

        // Act
        sut.DebugLocals[0].IsExpanded = true;

        // Assert
        var member = Assert.Single(sut.DebugLocals[0].Children);
        Assert.Equal(("Age", "31"), (member.Name, member.Value));
    }
}
