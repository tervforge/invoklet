using System.Security.Cryptography;
using Invoklet.Secrets;

namespace Invoklet.NonUI.Tests.Secrets;

// A Secret Store in a folder of the fixture's own, with a keychain in memory (the real one is never touched). The script's side reads
// the store named by INVOKLET_SECRETS_FILE with the key in INVOKLET_SECRETS_KEY: both variables are set here and put back when the
// fixture goes, and the folder goes with it.
internal sealed class FileSecretStoreKeyFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-secrets-tests").FullName;
    private readonly string? _key = Environment.GetEnvironmentVariable(SecretKeys.EnvVar);
    private readonly string? _file = Environment.GetEnvironmentVariable(FileSecretStore.FileEnvVar);

    public string StorePath => Path.Combine(_folder, "nested", "secrets.json");

    public FakeKeychain Keychain { get; } = new();

    public FileSecretStoreKeyFixture() => Environment.SetEnvironmentVariable(FileSecretStore.FileEnvVar, StorePath);

    public FileSecretStore CreateSut() => new(StorePath, Keychain);

    /// <summary>The store as an older Invoklet left it: values in plain text.</summary>
    public void HasPlainStore(params Secret[] secrets) => new FileSecretStore(StorePath).Save(secrets, null);

    /// <summary>The store encrypted with the keychain's key.</summary>
    public void HasEncryptedStore(params Secret[] secrets) => CreateSut().Save(secrets, null);

    /// <summary>The key a script started some other way is given in its environment; null for none.</summary>
    public void ScriptIsGivenKey(byte[]? key) =>
        Environment.SetEnvironmentVariable(SecretKeys.EnvVar, key is null ? null : Convert.ToBase64String(key));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecretKeys.EnvVar, _key);
        Environment.SetEnvironmentVariable(FileSecretStore.FileEnvVar, _file);
        Directory.Delete(_folder, recursive: true);
    }

    internal sealed class FakeKeychain : ISecretKeySource
    {
        public byte[]? Key { get; set; }

        public byte[]? Find() => Key;

        public byte[] GetOrCreate() => Key ??= RandomNumberGenerator.GetBytes(32);
    }
}
