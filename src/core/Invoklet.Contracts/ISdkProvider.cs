using Invoklet.Contracts.Models;

namespace Invoklet.Contracts;

public interface ISdkProvider
{
    /// <summary>
    /// Lists the installed .NET SDKs. <paramref name="dotnetExecutableOverride"/> lets a caller that
    /// knows about a user-configured dotnet path (e.g. <c>DebuggerSettings.DotnetPath</c>) use it
    /// instead of resolving the bare <c>dotnet</c> command via PATH or the platform's standard
    /// install locations — <see langword="null"/>/empty uses that default resolution.
    /// </summary>
    IReadOnlyList<DotnetSdk> Get(string? dotnetExecutableOverride = null);
}
