using Invoklet.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Secrets;

// No process Invoklet starts gets the Secret Store's key: a key in the environment of whoever started Invoklet goes no further.
[Trait("Platform", "Independent")]
public sealed class SecretKeysRemoveFromTests
{
    [Fact]
    public void RemoveFrom_AnEnvironmentWithTheKey_TakesTheKeyAndNothingElse()
    {
        // Arrange
        var environment = new Dictionary<string, string?> { [SecretKeys.EnvVar] = "aW5oZXJpdGVk", ["PATH"] = "/usr/bin" };

        // Act
        SecretKeys.RemoveFrom(environment);

        // Assert
        Assert.Equal(["PATH"], environment.Keys);
    }
}
