using System.Text;
using System.Text.Json;

namespace Invoklet.Runtime.Tests;

// A script started by Invoklet (INVOKLET_INPUT=1) whose stdin holds Invoklet's answers and whose stdout is kept: the console and the
// variable are what Util speaks to Invoklet through. Both are put back when the fixture goes.
internal sealed class UtilSecretRequestFixture : IDisposable
{
    private readonly TextWriter _out = Console.Out;
    private readonly TextReader _in = Console.In;
    private readonly string? _input = Environment.GetEnvironmentVariable(Util.InputEnvVar);
    private readonly StringWriter _written = new();

    // A name no other test has used, so nothing is remembered between tests.
    public string Name { get; } = "test-" + Guid.NewGuid().ToString("N");

    /// <summary>Invoklet answers with these lines, one per request: "1"+Base64 value, "0", "2"+Base64 reason, "3",
    /// "4"+Base64 reason.</summary>
    public UtilSecretRequestFixture(params string[] answers)
        : this(new StringReader(string.Concat(answers.Select(a => a + "\n"))))
    {
    }

    public UtilSecretRequestFixture(TextReader answers)
    {
        Environment.SetEnvironmentVariable(Util.InputEnvVar, "1");
        Console.SetOut(_written);
        Console.SetIn(answers);
    }

    public static string Found(string value) => "1" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static string Unreadable(string reason) => "2" + Convert.ToBase64String(Encoding.UTF8.GetBytes(reason));

    public static string Invalid(string reason) => "4" + Convert.ToBase64String(Encoding.UTF8.GetBytes(reason));

    /// <summary>The script was started some other way: nothing answers its requests.</summary>
    public void NotStartedByInvoklet() => Environment.SetEnvironmentVariable(Util.InputEnvVar, null);

    /// <summary>The first request the script wrote, as JSON.</summary>
    public JsonDocument FirstRequest()
    {
        var line = _written.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('\r');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(line[1..])));
    }

    public void Dispose()
    {
        Console.SetOut(_out);
        Console.SetIn(_in);
        Environment.SetEnvironmentVariable(Util.InputEnvVar, _input);
    }
}
