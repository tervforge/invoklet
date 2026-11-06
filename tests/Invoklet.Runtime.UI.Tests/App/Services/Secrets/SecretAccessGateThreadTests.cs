using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Invoklet.Contracts;
using Invoklet.Services;
using Invoklet.Services.Preferences;
using Invoklet.Services.Secrets;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Services.Secrets;

// A script's request arrives on a thread of the pool; the question is a dialog, and saving Always Allow tells the windows, which update
// their menus. Both must happen on the UI thread: saving from the pool threw, and the script was refused although the user had allowed it.
public class SecretAccessGateThreadTests
{
    [AvaloniaFact]
    public async Task ARequestFromAnotherThread_Should_AskAndSave_OnTheUiThread()
    {
        var settings = new SecretsSettings();
        var preferences = new Mock<IPreferencesService>();
        preferences.SetupGet(p => p.Secrets).Returns(settings);
        bool? savedOnUiThread = null, askedOnUiThread = null;
        preferences.Setup(p => p.Save()).Callback(() => savedOnUiThread = Dispatcher.UIThread.CheckAccess());
        var gate = new SecretAccessGate(preferences.Object, (_, _, _, _) =>
        {
            askedOnUiThread = Dispatcher.UIThread.CheckAccess();
            // The dialog's answer, finished off the UI thread.
            return Task.Run(() => SecretAccessChoice.Always);
        }, _ => "AAAA"); // the script's file, as the gate would read it

        var allowed = await Task.Run(() => gate.MayAccessOnUiThreadAsync(
            new SecretAccess(Path.Combine(Path.GetTempPath(), "report.inv"), "billing", IsSave: true), CancellationToken.None));

        Assert.True(allowed);
        Assert.True(askedOnUiThread);
        Assert.True(savedOnUiThread);
        Assert.Single(settings.AllowedScripts);
    }
}
