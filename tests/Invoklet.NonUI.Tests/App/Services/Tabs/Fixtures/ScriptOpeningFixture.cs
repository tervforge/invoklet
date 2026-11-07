using System.Text;
using Invoklet.Format.MdRun;
using Invoklet.Services.Examples;
using Invoklet.Services.Recovery;
using Invoklet.Services.Tabs;

namespace Invoklet.NonUI.Tests.App.Services.Tabs.Fixtures;

// Files in memory, by path, for the decision of what a tab opens with.
internal sealed class ScriptOpeningFixture
{
    public const string Path = "script.inv";
    public const string NewCode = "// new";

    public Dictionary<string, byte[]> Files { get; } = [];

    public static string Script(string code, string language = "csharp") =>
        $"---\nversion: 1\nlanguage: {language}\n---\n\n```{language} run\n{code}\n```\n";

    public void Write(string text) => Files[Path] = Encoding.UTF8.GetBytes(text);

    public static ExampleScript Example(string code) => new("hello", "Hello", Script(code), MdRunLanguage.CSharp, MdRunKind.Statements);

    public OpenedScript Open(RecoveryTab? recovered = null, string? filePath = null, ExampleScript? example = null) =>
        ScriptOpening.Open(recovered, filePath, example, MdRunLanguage.CSharp, MdRunKind.Program, () => NewCode,
            Files.ContainsKey, path => Files[path]);
}
