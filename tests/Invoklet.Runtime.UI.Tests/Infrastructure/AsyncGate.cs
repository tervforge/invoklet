namespace Invoklet.Runtime.UI.Tests.Infrastructure;

/// <summary>A gate a fake waits at without holding a thread: closed by <see cref="Reset"/>, opened by <see cref="Set"/>.</summary>
internal sealed class AsyncGate(bool open)
{
    private readonly object _lock = new();
    private TaskCompletionSource _opened = New(open);

    public void Set()
    {
        lock (_lock) _opened.TrySetResult();
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (_opened.Task.IsCompleted) _opened = New(false);
        }
    }

    public Task WaitAsync()
    {
        lock (_lock) return _opened.Task;
    }

    private static TaskCompletionSource New(bool open)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (open) source.SetResult();
        return source;
    }
}
