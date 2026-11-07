using Avalonia.Threading;

namespace Invoklet.NonUI.Tests.Infrastructure;

/// <summary>
/// The UI thread's dispatcher for a test, without threads: what the code under test posts waits in a queue until the test runs it
/// with <see cref="RunPosted"/>, and only while that runs is it "on the UI thread". An await inside posted work comes back through
/// the queue, as it comes back to the UI thread in the app.
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
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new QueueContext(this));
        _running = true;
        try
        {
            while (_posted.TryDequeue(out var action)) action();
        }
        finally
        {
            _running = false;
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class QueueContext(QueuedDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(() => callback(state));

        public override SynchronizationContext CreateCopy() => this;
    }
}
