using System.Reflection;
using Avalonia.Threading;
using Invoklet.Runtime.UI.Tests.Infrastructure;
using Xunit.v3;

[assembly: FlushUiQueueAfterEachTest]

namespace Invoklet.Runtime.UI.Tests.Infrastructure;

/// <summary>
/// After every test, runs what it left in the UI queue (a window shown and closed leaves compositor work there). Work left for the next
/// test's isolated application made its set-up fail now and then, on a test unrelated to the one that left it ("the calling thread cannot
/// access this object because a different thread owns it"), as CodeTextEditorCompletionIntegrationTests found for one of its tests.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FlushUiQueueAfterEachTest : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.RunJobs();
    }
}
