using Invoklet.Secrets;
using Invoklet.Services.Preferences;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// The Secret Store window over an empty store and settings that allow two scripts always.
internal sealed class SecretStoreWindowViewModelRevokeFixture
{
    public Mock<ISecretStore> Store { get; } = new();

    public Mock<IPreferencesService> Preferences { get; } = new();

    public SecretsSettings Settings { get; } = new()
    {
        AllowedScripts = ["/a/report.inv", "/a/other.inv"],
        AllowedScriptHashes = new() { ["/a/report.inv"] = "AAAA", ["/a/other.inv"] = "BBBB" },
    };

    public SecretStoreWindowViewModelRevokeFixture()
    {
        Store.Setup(s => s.LoadSnapshot()).Returns(new SecretStoreSnapshot([], null));
        Preferences.SetupGet(p => p.Secrets).Returns(Settings);
    }

    public SecretStoreWindowViewModel CreateSut() => new(Store.Object, Preferences.Object);
}
