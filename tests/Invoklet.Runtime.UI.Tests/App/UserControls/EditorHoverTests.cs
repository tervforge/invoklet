using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Xunit;
using static Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures.EditorHoverFixture;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// What the editor shows for the symbol under the pointer once it stops: Quick Info, the message of a diagnostic, or while the debugger
// is paused the symbol's value in a tip of its own.
public sealed class EditorHoverTests
{
    private const int OverFirst = 6; // "fi|rst"

    [AvaloniaFact]
    public async Task PointerStops_OverASymbol_ShowsItsQuickInfo()
    {
        // Arrange
        using var fixture = new EditorHoverFixture();
        using var sut = fixture.CreateSut();

        // Act
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.Tip is null; });

        // Assert
        Assert.NotNull(fixture.Tip);
        Assert.IsNotType<string>(fixture.Tip);
    }

    [AvaloniaFact]
    public async Task PointerStops_OverADiagnostic_ShowsItsMessage()
    {
        // Arrange
        using var fixture = new EditorHoverFixture();
        fixture.Diagnostics.Add(DiagnosticOverFirst());
        using var sut = fixture.CreateSut();

        // Act
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.Tip is null; });

        // Assert
        Assert.Equal("boom", fixture.Tip);
    }

    [AvaloniaFact]
    public async Task PointerStops_OverALocalWhilePaused_ShowsItsValueInATip()
    {
        // Arrange
        using var fixture = new EditorHoverFixture { DebugContext = PausedWithFirst() };
        using var sut = fixture.CreateSut();

        // Act
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.ValueTip is null; });

        // Assert
        Assert.NotNull(fixture.ValueTip);
        Assert.Null(fixture.Tip);
    }

    [AvaloniaFact]
    public async Task Invalidate_AfterAValueTip_TakesItAway()
    {
        // Arrange
        using var fixture = new EditorHoverFixture { DebugContext = PausedWithFirst() };
        using var sut = fixture.CreateSut();
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.ValueTip is null; });

        // Act
        sut.Invalidate();

        // Assert
        Assert.Null(fixture.ValueTip);
        Assert.Null(fixture.Tip);
    }

    [AvaloniaFact]
    public async Task Dispose_AfterAValueTip_ClosesIt()
    {
        // Arrange
        using var fixture = new EditorHoverFixture { DebugContext = PausedWithFirst() };
        var sut = fixture.CreateSut();
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.ValueTip is null; });

        // Act
        sut.Dispose();

        // Assert
        Assert.Null(fixture.ValueTip);
    }

    [AvaloniaFact]
    public async Task PointerLeavesTheEditor_WithAValueTip_ClosesItAfterTheGrace()
    {
        // Arrange
        using var fixture = new EditorHoverFixture { DebugContext = PausedWithFirst() };
        using var sut = fixture.CreateSut();
        fixture.Window.MouseMove(fixture.PointAt(OverFirst));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.ValueTip is null; });

        // Act: past the end of the line, away from the symbol and not into the tip
        fixture.Window.MouseMove(new global::Avalonia.Point(590, 290));
        await TestWait.WhileAsync(() => { Dispatcher.UIThread.RunJobs(); return fixture.ValueTip is not null; });

        // Assert
        Assert.Null(fixture.ValueTip);
    }
}
