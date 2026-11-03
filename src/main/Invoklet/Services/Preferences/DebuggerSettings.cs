using Invoklet.Languages.DotnetCommon.Sdk;

namespace Invoklet.Services.Preferences;

public sealed class DebuggerSettings
{
    // "dotnet" resolves through PATH and the platform's standard install locations. Users who need
    // a specific install can override this in Preferences or the first-run setup wizard.
    public string DotnetPath { get; set; } = DotnetExecutableResolver.Resolve("dotnet");
    public string DebuggerPath { get; set; } = "";
}
