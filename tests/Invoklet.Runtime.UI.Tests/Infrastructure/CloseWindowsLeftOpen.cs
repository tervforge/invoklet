using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Invoklet.Avalonia.CodeTextEditor;
using Xunit.v3;

[assembly: Invoklet.Runtime.UI.Tests.Infrastructure.CloseWindowsLeftOpen]

namespace Invoklet.Runtime.UI.Tests.Infrastructure;

// Closes, after each test, the windows it left open, and lets go of their code editors. A test that ends without closing its window
// left it to the rest of the run: an editor in it keeps a thread for its highlighting until it is disposed, and some three hundred of
// them made the whole run slower and its timing-sensitive tests fail on a slow machine.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CloseWindowsLeftOpen : BeforeAfterTestAttribute
{
    private static readonly HashSet<Window> Open = [];
    private static bool _watching;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (_watching || !Dispatcher.UIThread.CheckAccess()) return;
        _watching = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Open.Add(window), RoutingStrategies.Direct);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window), RoutingStrategies.Direct);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!Dispatcher.UIThread.CheckAccess()) return;
        foreach (var window in Open.ToList())
        {
            var editors = window.GetVisualDescendants().OfType<CodeTextEditor>().ToList();
            // What the test left is no longer its business: a window that will not close, or a handler that fails on the way, does not fail it.
            try { window.Close(); }
            catch (Exception) { }
            Open.Remove(window);
            foreach (var editor in editors) editor.Dispose();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
