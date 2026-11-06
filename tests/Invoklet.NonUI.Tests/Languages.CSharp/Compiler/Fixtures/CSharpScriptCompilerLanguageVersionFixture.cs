using Invoklet.Languages.DotnetCommon.Sdk;

namespace Invoklet.NonUI.Tests.Languages.CSharp.Compiler.Fixtures;

// The targets on either side of the newest one Invoklet offers.
internal sealed class CSharpScriptCompilerLanguageVersionFixture
{
    public int NewestOffered { get; } = DotnetSdkProvider.MaxSupportedSdkMajorVersion;

    public int FirstNotOffered { get; } = DotnetSdkProvider.MaxSupportedSdkMajorVersion + 1;
}
