namespace Invoklet.Runtime;

/// <summary>A source-code fragment emitted by <c>Util.Code</c>.</summary>
internal sealed class CodeBlock
{
    public CodeBlock(string source, CodeLanguage language)
    {
        Source = source;
        Language = language;
    }

    public string Source { get; }
    public CodeLanguage Language { get; }
}
