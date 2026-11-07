using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;
using Xunit;
using static Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures.EditorParameterInfoFixture;

namespace Invoklet.Runtime.UI.Tests.App.UserControls;

// Parameter info: a row per overload, its signature, and under it the descriptions of the method and the active parameter.
public sealed class EditorParameterInfoTests
{
    [AvaloniaFact]
    public async Task ProvideAsync_TwoOverloads_AddsARowForEach()
    {
        // Arrange
        using var fixture = new EditorParameterInfoFixture { Result = Help(Overload(null), Overload(null)) };
        var request = fixture.Request();

        // Act
        await fixture.CreateSut().ProvideAsync(request);

        // Assert
        Assert.Equal(2, request.Overloads.Count);
        Assert.All(request.Overloads, row => Assert.NotNull(row.Header));
    }

    [AvaloniaFact]
    public async Task ProvideAsync_DescribedMethodAndParameter_ShowsBothUnderTheSignature()
    {
        // Arrange
        using var fixture = new EditorParameterInfoFixture { Result = Help(Overload("Writes a line.", Parameter("value", "What to write."))) };
        var request = fixture.Request();

        // Act
        await fixture.CreateSut().ProvideAsync(request);

        // Assert
        var content = Assert.IsType<TextBlock>(Assert.Single(request.Overloads).Content);
        Assert.Equal("Writes a line.\n\nvalue: What to write.", content.Text);
    }

    [AvaloniaFact]
    public async Task ProvideAsync_NothingDescribed_HasNoTextUnderTheSignature()
    {
        // Arrange
        using var fixture = new EditorParameterInfoFixture { Result = Help(Overload(null, Parameter("value", null))) };
        var request = fixture.Request();

        // Act
        await fixture.CreateSut().ProvideAsync(request);

        // Assert
        Assert.Null(Assert.Single(request.Overloads).Content);
    }

    [AvaloniaFact]
    public async Task ProvideAsync_NoSignatureHelp_AddsNothing()
    {
        // Arrange
        using var fixture = new EditorParameterInfoFixture();
        var request = fixture.Request();

        // Act
        await fixture.CreateSut().ProvideAsync(request);

        // Assert
        Assert.Empty(request.Overloads);
    }

    [AvaloniaFact]
    public async Task ProvideAsync_ANarrowEditor_KeepsTheDescriptionReadable()
    {
        // Arrange: an editor never laid out has no width
        using var fixture = new EditorParameterInfoFixture { Result = Help(Overload("Writes a line.")) };
        var request = fixture.Request();

        // Act
        await fixture.CreateSut().ProvideAsync(request);

        // Assert
        Assert.Equal(320, Assert.IsType<TextBlock>(Assert.Single(request.Overloads).Content).MaxWidth);
    }
}
