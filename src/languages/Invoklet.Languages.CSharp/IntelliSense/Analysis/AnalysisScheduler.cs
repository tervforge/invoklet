namespace Invoklet.Languages.CSharp.IntelliSense.Analysis;

/// <summary>
/// Runs the background analysis that follows an edit: it waits out a debounce, asks for the text only then (a notification
/// superseded by the next keystroke never builds its string), analyses it, and starts again on the same text when the analysis
/// reports that what it was built on changed meanwhile. A newer notification cancels the pass in flight. It knows nothing of
/// Roslyn: what is analysed is the delegate it is given.
/// </summary>
/// <param name="debounce">Read at every notification, so it can be changed between two.</param>
/// <param name="run">Starts the pass; the service wraps it in the culture its Roslyn messages are produced in.</param>
/// <param name="onFailure">Told of what a pass threw, other than being cancelled; the scheduler goes on.</param>
/// <param name="stopTimeout">How long <see cref="StopAsync"/> waits for a pass that does not notice its cancellation.</param>
internal sealed class AnalysisScheduler(
    Func<TimeSpan> debounce, Func<Func<Task>, Task> run, Action<Exception> onFailure, TimeSpan stopTimeout)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private Task? _pass;
    private Func<string>? _getCode;
    private bool _stopped;
    private long _generation;

    private bool Stopped
    {
        get
        {
            lock (_gate) return _stopped;
        }
    }

    /// <summary>The number of the notification made last; for tests.</summary>
    internal long Generation
    {
        get
        {
            lock (_gate) return _generation;
        }
    }

    /// <summary>The pass last started; for tests to await.</summary>
    internal Task? Pass
    {
        get
        {
            lock (_gate) return _pass;
        }
    }

    /// <param name="getCode">Reads the text of the editor; called once the debounce is over, on the thread of the pass.</param>
    /// <param name="analyze">
    /// Analyses the text and says whether its result is still valid. False (the project context changed during the pass) makes
    /// the scheduler analyse the same text again, unless a newer notification has taken over.
    /// </param>
    public void Notify(Func<string> getCode, Func<string, CancellationToken, Task<bool>> analyze) => Start(getCode, analyze, repeatOf: null);

    public void Refresh(Func<string, CancellationToken, Task<bool>> analyze)
    {
        Func<string>? getCode;
        long generation;
        lock (_gate)
        {
            getCode = _getCode;
            generation = _generation;
        }
        if (getCode is not null) Repeat(generation, getCode, analyze);
    }

    /// <summary>
    /// Analyses again the text of the pass of <paramref name="generation"/>, whose result was stale — unless a newer notification
    /// has come since. That is checked under the lock a notification takes: the pass's own look at its token comes earlier, and a
    /// notification made in between would otherwise be cancelled by this repeat, its text never analysed.
    /// </summary>
    internal void Repeat(long generation, Func<string> getCode, Func<string, CancellationToken, Task<bool>> analyze) =>
        Start(getCode, analyze, generation);

    private void Start(Func<string> getCode, Func<string, CancellationToken, Task<bool>> analyze, long? repeatOf)
    {
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            // After Stop nothing starts again, not even the repeat of a pass that was still running: its owner is gone.
            if (_stopped) return;
            if (repeatOf is { } repeated && repeated != _generation) return;
            // Not disposed: a superseded pass can still be past its checks of the token (inside a code fix provider that does not
            // poll it), and reading a disposed source throws. A source whose wait handle is never touched holds no unmanaged
            // resources, so the superseded one is left to the collector; Stop disposes the last one.
            _current?.Cancel();
            _current = new CancellationTokenSource();
            _getCode = getCode;
            token = _current.Token;
            generation = ++_generation;
        }

        // Started outside the lock: a runner that executes the pass on this very thread would otherwise hold it for the whole pass, and
        // Stop could not even begin its bounded wait.
        var pass = run(async () =>
        {
            try
            {
                await Task.Delay(debounce(), token);
                var code = getCode();
                if (!await analyze(code, token) && !token.IsCancellationRequested) Repeat(generation, () => code, analyze);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Not told once stopped: the owner is gone.
                if (!Stopped) onFailure(ex);
            }
        });
        lock (_gate)
        {
            // A notification made meanwhile (even from inside the pass) is the newer one.
            if (generation == _generation) _pass = pass;
        }
    }

    /// <summary>
    /// Cancels the pass in flight; the task ends when the pass has, or after the timeout: a pass that never checks its token must
    /// not be able to hold the end of the editor for ever. Nothing starts after this is called.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? source;
        Task? pass;
        lock (_gate)
        {
            _stopped = true;
            _getCode = null;
            source = _current;
            pass = _pass;
        }

        source?.Cancel();
        try
        {
            if (pass is not null) await pass.WaitAsync(stopTimeout);
        }
        catch
        {
            // Already reported from inside the pass, or the pass outlived the timeout.
        }

        source?.Dispose();
    }
}
