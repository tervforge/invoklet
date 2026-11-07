using System.Runtime.CompilerServices;

namespace Invoklet.NonUI.Tests.Infrastructure;

// The tests of this assembly run in parallel, and some block pool threads for seconds (a read that hangs, a process that is
// waited for). Once the pool's minimum is reached it adds a thread a second, so on a runner with few cores work a test started
// with Task.Run could wait longer than the test's own time limit. The minimum is raised once here instead of in each test class.
internal static class TestThreadPoolSetup
{
    [ModuleInitializer]
    internal static void RaiseMinimumThreads() => ThreadPool.SetMinThreads(512, 512);
}
