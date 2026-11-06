using Xunit;

namespace Invoklet.Runtime.Tests;

// A script Invoklet runs asks it for one secret by name and gets the value, none, the reason the store can't be read, or a refusal; the
// store's key never reaches the script. Console and environment are process-wide: see ConsoleTestCollection.
[Collection(ConsoleTestCollection.Name)]
public sealed class UtilSecretRequestTests
{
    [Fact]
    public void GetSecret_AStoredSecret_ReturnsTheValueInvokletAnswers()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Found("p@ss\nwörd"));

        // Act
        var value = Util.GetSecret(fixture.Name);

        // Assert
        Assert.Equal("p@ss\nwörd", value);
    }

    [Fact]
    public void GetSecret_AnyName_AsksInvokletToReadThatSecret()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Found("x"));

        // Act
        Util.GetSecret(fixture.Name);

        // Assert
        using var request = fixture.FirstRequest();
        Assert.Equal(fixture.Name, request.RootElement.GetProperty("Secret").GetString());
        Assert.True(request.RootElement.GetProperty("Read").GetBoolean());
    }

    [Fact]
    public void GetSecret_AStoreInvokletCantRead_ThrowsWithItsReason()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Unreadable("damaged"));

        // Act
        var thrown = Assert.Throws<InvalidDataException>(() => Util.GetSecret(fixture.Name));

        // Assert
        Assert.Equal("damaged", thrown.Message);
    }

    // Invoklet answers 3 when the user didn't let the script use the Secret Store.
    [Fact]
    public void GetSecret_TheUserDidntAllowIt_ThrowsAsNotAllowed()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("3");

        // Act
        var read = () => Util.GetSecret(fixture.Name);

        // Assert
        Assert.Throws<UnauthorizedAccessException>(read);
    }

    [Fact]
    public void SetSecret_TheUserDidntAllowIt_ThrowsAsNotAllowed()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("3");

        // Act
        var save = () => Util.SetSecret(fixture.Name, "x");

        // Assert
        Assert.Throws<UnauthorizedAccessException>(save);
    }

    // An empty answer to the question for a missing secret (the command line's stdin ended, say) is no secret.
    [Fact]
    public void GetSecret_AnEmptyAnswerForAMissingSecret_ThrowsAsMissing()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("0", "");

        // Act
        var read = () => Util.GetSecret(fixture.Name);

        // Assert
        Assert.Throws<KeyNotFoundException>(read);
    }
}
