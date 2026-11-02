namespace Invoklet.Services;

/// <summary>
/// The work of the workspace that is still going on: operations of the file menu and the file tree that were
/// started, and the saves of the tabs. A snapshot of the session, or the closing of the window, waits until it is over,
/// so that it sees the state either before an operation or after it, never half of it. Used on the UI thread.
/// </summary>
internal sealed class WorkspaceWork(TimeSpan? readyTimeout = null)
{
    private readonly HashSet<Task> _operations = [];
    private readonly TimeSpan _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(20);

    /// <summary>Starts the operation and remembers it until it ends; returns its task.</summary>
    public Task Track(Func<Task> operation)
    {
        _operations.RemoveWhere(task => task.IsCompleted);
        var task = operation();
        if (!task.IsCompleted) _operations.Add(task);
        return task;
    }

    /// <summary>
    /// Returns the included tabs once every one of them is ready (its editor exists: a closing window must not skip a
    /// tab whose editor is still being created) and neither a tracked operation nor a save of any tab is going on, both
    /// seen in the same look at the tabs: whatever begins while it waits (a save, a tab) makes it look again.
    /// </summary>
    /// <exception cref="TimeoutException">A tab is not ready in time.</exception>
    public async Task<List<T>> WaitUntilIdleAsync<T>(
        Func<IReadOnlyList<T>> tabs, Func<T, bool> include, Func<T, Task> ready, Func<T, IEnumerable<Task?>> saves)
    {
        while (true)
        {
            var included = tabs().Where(include).ToList();
            // A tab whose readiness failed is awaited too, so that its error is raised.
            var notReady = included.Select(ready).Where(task => !task.IsCompletedSuccessfully).ToArray();
            // A Save As owns the rebasing of paths as well as the write: a snapshot must see both or neither.
            var pending = tabs().SelectMany(saves).OfType<Task>().Concat(_operations)
                .Where(task => !task.IsCompleted).Distinct().ToArray();
            if (notReady.Length == 0 && pending.Length == 0)
            {
                _operations.RemoveWhere(task => task.IsCompleted);
                return included;
            }

            if (notReady.Length > 0) await Task.WhenAll(notReady).WaitAsync(_readyTimeout);
            if (pending.Length > 0) await Task.WhenAll(pending);
        }
    }
}
