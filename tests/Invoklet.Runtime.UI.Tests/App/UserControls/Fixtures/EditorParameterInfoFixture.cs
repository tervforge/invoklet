using Invoklet.Avalonia.CodeTextEditor;
using Invoklet.Contracts.IntelliSense;
using Invoklet.UserControls;
using Moq;

namespace Invoklet.Runtime.UI.Tests.App.UserControls.Fixtures;

// An editor and a language service whose signature help is whatever the test sets in Result.
internal sealed class EditorParameterInfoFixture : IDisposable
{
    public EditorParameterInfoFixture()
    {
        Service.Setup(s => s.GetSignatureHelpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<char?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result);
    }

    public CodeTextEditor Editor { get; } = new() { Text = "Console.WriteLine(" };
    public Mock<IIntelliSenseService> Service { get; } = new();
    public ISignatureHelpResult? Result { get; set; }

    public EditorParameterInfo CreateSut() => new(Service.Object, Editor, () => false);

    public static ISignatureHelpItem Overload(string? summary, params ISignatureHelpParameter[] parameters) =>
        Mock.Of<ISignatureHelpItem>(i => i.Signature == new[] { new DisplayTextSegment("WriteLine()", SemanticTokenKind.MethodName) }
            && i.Summary == summary && i.Parameters == parameters && i.ActiveParameterIndex == 0);

    public static ISignatureHelpParameter Parameter(string name, string? summary) =>
        Mock.Of<ISignatureHelpParameter>(p => p.Name == name && p.Summary == summary
            && p.Signature == Array.Empty<DisplayTextSegment>());

    public static ISignatureHelpResult Help(params ISignatureHelpItem[] items) =>
        Mock.Of<ISignatureHelpResult>(r => r.Items == items);

    public OverloadRequestedEventArgs Request() => new('(', Editor.Text.Length);

    public void Dispose() => Editor.Dispose();
}
