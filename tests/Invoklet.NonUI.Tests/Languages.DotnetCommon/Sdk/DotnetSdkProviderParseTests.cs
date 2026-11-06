using Invoklet.Languages.DotnetCommon.Sdk;
using Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk.Fixtures;
using Xunit;

namespace Invoklet.NonUI.Tests.Languages.DotnetCommon.Sdk;

// Which installed SDKs a script can target: those the compiler Invoklet is built with knows, the newest of each target first.
[Trait("Platform", "Independent")]
public sealed class DotnetSdkProviderParseTests
{
    [Fact]
    public void Parse_AnSdkNewerThanTheCompilerKnows_LeavesItOut()
    {
        // Arrange
        var fixture = new DotnetSdkProviderParseFixture();

        // Act
        var sdks = DotnetSdkProvider.Parse(fixture.WithAPreviewNewerThanTheCompiler);

        // Assert
        Assert.Equal(["net10.0", "net8.0"], sdks.Select(s => s.TargetFrameworkMoniker));
    }

    [Fact]
    public void Parse_AnSdkOlderThanSupported_LeavesItOut()
    {
        // Arrange
        var fixture = new DotnetSdkProviderParseFixture();

        // Act
        var sdks = DotnetSdkProvider.Parse(fixture.WithAnUnsupportedOldOne);

        // Assert
        Assert.Equal(["net8.0"], sdks.Select(s => s.TargetFrameworkMoniker));
    }

    [Fact]
    public void Parse_TwoSdksOfOneTarget_OffersTheNewer()
    {
        // Arrange
        var fixture = new DotnetSdkProviderParseFixture();

        // Act
        var sdks = DotnetSdkProvider.Parse(fixture.WithTwoOfOneTarget);

        // Assert
        Assert.Equal(".NET 10.0.403", Assert.Single(sdks).Description);
    }
}
