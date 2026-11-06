using Invoklet.Contracts;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Services.Secrets;
using Moq;

namespace Invoklet.NonUI.Tests.App.Services.Secrets.Fixtures;

// The gate over fake settings; the user's answer to the question is Answer, and every question asked is in Asked.
internal sealed class SecretAccessGateFixture
{
    public SecretsSettings Settings { get; } = new();

    public Mock<IPreferencesService> Preferences { get; } = new();

    public SecretAccessChoice Answer { get; set; } = SecretAccessChoice.Always;

    public List<(string Script, string Secret, bool IsSave)> Asked { get; } = [];

    // A question still open: the answer comes when the test sets it.
    public TaskCompletionSource<SecretAccessChoice>? OpenQuestion { get; set; }

    // The run is stopped while the question is open, before the user answers.
    public CancellationTokenSource? StoppedWhileAsking { get; set; }

    // The user's answer to a change of a kept secret, and the changes asked about (script, secret, removes).
    public bool AllowChange { get; set; } = true;

    public List<(string Script, string Secret, bool Remove)> ChangesAsked { get; } = [];

    public string ScriptPath { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "report.inv"));

    // What the script's file holds, as its SHA-256: changed by the test as another program would; nothing is read from the disk.
    public string? FileContents { get; set; } = "AAAA";

    public SecretAccessGateFixture() => Preferences.SetupGet(p => p.Secrets).Returns(Settings);

    public SecretAccess Request(string secret = "billing", bool isSave = false, bool unsaved = false, SecretChange change = SecretChange.None) =>
        new(unsaved ? null : ScriptPath, secret, isSave, change);

    public SecretAccessGate CreateSut() => new(Preferences.Object, (script, secret, isSave, token) =>
    {
        Asked.Add((script, secret, isSave));
        StoppedWhileAsking?.Cancel();
        if (OpenQuestion is { } open) return open.Task;
        return Task.FromResult(token.IsCancellationRequested ? SecretAccessChoice.Deny : Answer);
    }, _ => FileContents, (script, secret, remove, _) =>
    {
        ChangesAsked.Add((script, secret, remove));
        return Task.FromResult(AllowChange);
    });

    /// <summary>The SHA-256 (hex) of <paramref name="bytes"/>, as the gate keeps a file's contents.</summary>
    public static string HashOf(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
}
