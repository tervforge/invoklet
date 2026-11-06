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

    public string ScriptPath { get; } = Path.Combine(Path.GetTempPath(), "report.inv");

    public SecretAccessGateFixture() => Preferences.SetupGet(p => p.Secrets).Returns(Settings);

    public SecretAccess Request(string secret = "billing", bool isSave = false, bool unsaved = false) =>
        new(unsaved ? null : ScriptPath, secret, isSave);

    public SecretAccessGate CreateSut() => new(Preferences.Object, (script, secret, isSave, token) =>
    {
        Asked.Add((script, secret, isSave));
        StoppedWhileAsking?.Cancel();
        if (OpenQuestion is { } open) return open.Task;
        return Task.FromResult(token.IsCancellationRequested ? SecretAccessChoice.Deny : Answer);
    });
}
