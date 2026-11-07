using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts;
using Invoklet.Contracts.Debugging;
using Invoklet.UserControls;
using Moq;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;

// An editor of three lines and, when Running is set, a debug session that takes breakpoints and records what it is sent.
internal sealed class EditorBreakpointsFixture : IDisposable
{
    public const string SourceFile = "Script.cs";

    public EditorBreakpointsFixture()
    {
        Host.Setup(h => h.Supports(LanguageHostCapabilities.Breakpoints)).Returns(true);
        Host.Setup(h => h.AddBreakpoint(It.IsAny<IBreakpoint>())).Callback<IBreakpoint>(Added.Add);
        Host.Setup(h => h.RemoveBreakpoint(It.IsAny<IBreakpoint>())).Callback<IBreakpoint>(b => Removed.Add(b.Line));
    }

    public CodeTextEditor Editor { get; } = new() { Text = "one\ntwo\nthree" };
    public Mock<ILanguageHost> Host { get; } = new();
    public bool Running { get; set; }
    public List<IBreakpoint> Added { get; } = [];
    public List<int> Removed { get; } = [];

    public EditorBreakpoints CreateSut()
    {
        var sut = new EditorBreakpoints(Editor, () => Running ? Host.Object : null, () => Running ? SourceFile : null);
        Editor.BreakpointAdded += sut.OnAdded;
        Editor.BreakpointRemoved += sut.OnRemoved;
        return sut;
    }

    public global::Invoklet.Avalonia.CodeTextEditor.Breakpoints.IBreakpoint At(int line) => Editor.Breakpoints.Single(b => b.Line == line);

    public static IBreakpoint Reported(int line) => Mock.Of<IBreakpoint>(b => b.Line == line && b.FileName == SourceFile);

    public void Dispose() => Editor.Dispose();
}
