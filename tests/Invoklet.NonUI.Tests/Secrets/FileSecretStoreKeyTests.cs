using Invoklet.NonUI.Tests.Infrastructure;
using Invoklet.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Secrets;

// The Secret Store's values are encrypted with a key kept in the system keychain, which never goes to a script: Invoklet answers a
// script's requests itself. A script started some other way reads the store only with a key it was given. On the real file system and
// environment, so in the collection that runs alone.
[Collection(RealBuildTestCollection.Name)]
public sealed class FileSecretStoreKeyTests
{
    // Util.SetSecret(name, null): the secret of that name, in any letter case, goes.
    [Fact]
    public void Put_NullForAStoredName_RemovesThatSecret()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        var sut = fixture.CreateSut();
        sut.Save([new Secret("Orders-DB", "old"), new Secret("zeta", "z")], null);

        // Act
        sut.Put("orders-db", null);

        // Assert
        Assert.Equal([new Secret("zeta", "z")], sut.Load());
    }

    [Fact]
    public void Put_NullForANameTheStoreLacks_WritesNothing()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        var sut = fixture.CreateSut();
        var version = sut.Save([new Secret("zeta", "z")], null);

        // Act
        sut.Put("missing", null);

        // Assert
        Assert.Equal(version, sut.LoadSnapshot().Version);
    }

    [Fact]
    public void EncryptIfPlain_AStoreAnOlderInvokletLeftPlain_EncryptsItAndKeepsTheValues()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        fixture.HasPlainStore(new Secret("db", "plain-secret-value"));
        var sut = fixture.CreateSut();

        // Act
        sut.EncryptIfPlain();

        // Assert
        Assert.True(SecretKeys.IsEncrypted(fixture.StorePath));
        Assert.DoesNotContain("plain-secret-value", File.ReadAllText(fixture.StorePath));
        Assert.Equal("plain-secret-value", sut.Read("DB"));
    }

    // The key stays in the keychain: encrypting puts it in no environment a script would inherit.
    [Fact]
    public void EncryptIfPlain_APlainStore_ExportsNoKey()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        fixture.HasPlainStore(new Secret("db", "plain-secret-value"));
        fixture.ScriptIsGivenKey(null);
        var sut = fixture.CreateSut();

        // Act
        sut.EncryptIfPlain();

        // Assert
        Assert.Null(Environment.GetEnvironmentVariable(SecretKeys.EnvVar));
    }

    [Fact]
    public void GetSecret_AScriptGivenTheKey_ReadsTheValue()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        fixture.HasEncryptedStore(new Secret("Orders-DB", "p@ss wörd"));
        fixture.ScriptIsGivenKey(fixture.Keychain.Key);

        // Act
        var value = Util.GetSecret("orders-db");

        // Assert
        Assert.Equal("p@ss wörd", value);
    }

    [Fact]
    public void GetSecret_AScriptGivenNoKey_SaysToRunItFromInvoklet()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        fixture.HasEncryptedStore(new Secret("Orders-DB", "p@ss wörd"));
        fixture.ScriptIsGivenKey(null);

        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => Util.GetSecret("orders-db"));

        // Assert
        Assert.Contains("run the script from Invoklet", thrown.Message);
    }

    [Fact]
    public void GetSecret_AScriptGivenAnotherKey_CantDecryptTheValue()
    {
        // Arrange
        using var fixture = new FileSecretStoreKeyFixture();
        fixture.HasEncryptedStore(new Secret("Orders-DB", "p@ss wörd"));
        fixture.ScriptIsGivenKey(new byte[32]);

        // Act
        var read = () => Util.GetSecret("orders-db");

        // Assert
        Assert.Throws<InvalidDataException>(read);
    }
}
