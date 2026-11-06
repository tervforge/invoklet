namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk.Fixtures;

// Lines as `dotnet --list-sdks` writes them.
internal sealed class DotnetSdkProviderParseFixture
{
    public IReadOnlyList<string> WithAPreviewNewerThanTheCompiler { get; } =
        ["8.0.427 [/usr/local/share/dotnet/sdk]", "10.0.401 [/usr/local/share/dotnet/sdk]", "11.0.100-rc.3.26522.148 [/usr/local/share/dotnet/sdk]"];

    public IReadOnlyList<string> WithAnUnsupportedOldOne { get; } = ["5.0.408 [/usr/local/share/dotnet/sdk]", "8.0.427 [/usr/local/share/dotnet/sdk]"];

    public IReadOnlyList<string> WithTwoOfOneTarget { get; } = ["10.0.401 [/usr/local/share/dotnet/sdk]", "10.0.403 [/Users/me/.dotnet/sdk]"];
}
