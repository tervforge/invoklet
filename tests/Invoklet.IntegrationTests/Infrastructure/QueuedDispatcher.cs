using Avalonia.Threading;

namespace Invoklet.IntegrationTests.Infrastructure;

/// <summary>
/// The dispatcher a control posts to from another thread, for a test without threads: what is posted waits in a queue until the test
/// runs it with <see cref="RunPosted"/>, and only while that runs is it "on the UI thread". It runs on the test's thread, which is
/// Avalonia's UI thread here, so what the posted work awaits comes back through Avalonia as in the app.
/// </summary>
internal sealed class QueuedDispatcher : IDispatcher
{
    private readonly Queue<Action> _posted = new();
    private bool _running;

    public int Pending => _posted.Count;

    public bool CheckAccess() => _running;

    public void VerifyAccess()
    {
        if (!_running) throw new InvalidOperationException("Not on the UI thread.");
    }

    public void Post(Action action, DispatcherPriority priority = default) => _posted.Enqueue(action);

    /// <summary>Runs what was posted, and what that posts in turn, until the queue is empty.</summary>
    public void RunPosted()
    {
        _running = true;
        try
        {
            while (_posted.TryDequeue(out var action)) action();
        }
        finally
        {
            _running = false;
        }
    }
}
