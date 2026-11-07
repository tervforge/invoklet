using Invoklet.Format.MdRun;
using Invoklet.NonUI.Tests.App.Services.Tabs.Fixtures;
using Invoklet.Services.Recovery;
using Invoklet.Services.Tabs;
using Invoklet.UserControls.Models;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services.Tabs;

[Trait("Platform", "Independent")]
public sealed class ScriptOpeningTests
{
    [Fact]
    public void Open_AValidFile_TakesItsCodeAndRemembersItsBytes()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();
        fixture.Write(ScriptOpeningFixture.Script("1.Dump();"));

        // Act
        var opened = fixture.Open(filePath: ScriptOpeningFixture.Path);

        // Assert
        Assert.Equal(ScriptSource.File, opened.Source);
        Assert.Equal("1.Dump();\n", opened.Text);
        Assert.NotNull(opened.Document);
        Assert.Null(opened.ParseError);
        Assert.True(ScriptFileState.Of(fixture.Files[ScriptOpeningFixture.Path]).SameAs(opened.DiskState!));
    }

    [Fact]
    public void Open_AnInvalidFile_TakesTheWholeFileAndSaysWhy()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();
        fixture.Write("no front matter here");

        // Act
        var opened = fixture.Open(filePath: ScriptOpeningFixture.Path);

        // Assert
        Assert.Equal("no front matter here", opened.Text);
        Assert.Null(opened.Document);
        Assert.NotNull(opened.ParseError);
    }

    [Fact]
    public void Open_AMissingFile_KnowsTheFileIsAbsent()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();

        // Act
        var opened = fixture.Open(filePath: ScriptOpeningFixture.Path);

        // Assert
        Assert.Equal(ScriptSource.Missing, opened.Source);
        Assert.Equal("", opened.Text);
        Assert.Same(ScriptFileState.Absent, opened.DiskState);
    }

    [Fact]
    public void Open_NoPath_IsANewScriptOfTheChosenKind()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();

        // Act
        var opened = fixture.Open();

        // Assert
        Assert.Equal(ScriptSource.New, opened.Source);
        Assert.Equal(ScriptOpeningFixture.NewCode, opened.Text);
        Assert.Equal(MdRunKind.Program, opened.Document?.FrontMatter.Kind);
        Assert.Null(opened.DiskState);
    }

    [Fact]
    public void Open_AnExampleWithoutPath_TakesItsCode()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();

        // Act
        var opened = fixture.Open(example: ScriptOpeningFixture.Example("\"hi\".Dump();"));

        // Assert
        Assert.Equal(ScriptSource.Example, opened.Source);
        Assert.Equal("\"hi\".Dump();\n", opened.Text);
        Assert.NotNull(opened.Document);
    }

    [Fact]
    public void Open_ARecoveredDraft_WinsOverItsFile()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();
        fixture.Write(ScriptOpeningFixture.Script("on disk"));
        var draft = new RecoveryTab { EditorText = "draft", ParseError = "bad", BaselineKnown = true, DiskHash = [1, 2] };

        // Act
        var opened = fixture.Open(draft, ScriptOpeningFixture.Path);

        // Assert
        Assert.Equal(ScriptSource.Recovered, opened.Source);
        Assert.Equal("draft", opened.Text);
        Assert.Equal("bad", opened.ParseError);
        Assert.True(new ScriptFileState([1, 2]).SameAs(opened.DiskState!));
        Assert.False(opened.RequiresBaselineConfirmation);
    }

    [Fact]
    public void Open_ARecoveredDraftWithoutBaseline_AsksForIt()
    {
        // Arrange
        var fixture = new ScriptOpeningFixture();

        // Act
        var opened = fixture.Open(new RecoveryTab { EditorText = "draft" }, ScriptOpeningFixture.Path);

        // Assert
        Assert.Null(opened.DiskState);
        Assert.True(opened.RequiresBaselineConfirmation);
    }

    [Fact]
    public void Read_AFileWithAByteOrderMark_DecodesWithoutIt()
    {
        // Arrange
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(ScriptOpeningFixture.Script("x"))).ToArray();

        // Act
        var opened = ScriptOpening.Read(bytes);

        // Assert
        Assert.Equal("x\n", opened.Text);
        Assert.NotNull(opened.Document);
    }
}
