using System.Text.Json.Serialization;
using Invoklet.Languages.DotnetCommon.Sdk;

namespace Invoklet.Services.Preferences;

public sealed class DebuggerSettings
{
    // Empty: found where it is when it is needed (DotnetExecutableResolver), so a .NET installed or moved later is found too. A
    // specific install is set in Preferences or by the first-run setup wizard.
    public string DotnetPath { get; set; } = "";

    /// <summary>The dotnet host to run: the one set, or the one found.</summary>
    [JsonIgnore]
    public string ResolvedDotnetPath => DotnetExecutableResolver.Resolve(DotnetPath);
    public string DebuggerPath { get; set; } = "";
}
