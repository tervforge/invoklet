using Avalonia.Threading;
using AvaloniaEdit.Document;
using Invoklet.Services;
using Invoklet.Services.Tabs;
using Invoklet.UserControls.Models;
using Microsoft.Extensions.Logging;

namespace Invoklet.UserControls;

// The heavy part of a tab, its text and its output, can wait on disk while the control stays as it is, so a
// tab that is not being used costs little. The control, the IntelliSense service and the metadata remain.
public partial class EditorsTabItemControl
{
    private bool _contentReleased;
    private Task<bool>? _restoring;

    // Counts the changes of the output (and of the folder it is kept in). A release takes the output at some count, and does not let go of the tab if it
    // is not that count when the write is done.
    private int _outputVersion;

    // The writes of the output of this tab, in the order they were asked for: a chain, extended on the UI thread only. They run one after another, so that an
    // older output is never written after a newer one (each is taken when it is asked for), and off the UI thread, so that a slow disk is not waited for.
    private Task _outputWrites = Task.CompletedTask;

    /// <summary>The last of the writes of the output that were asked for: complete when all of them are done.</summary>
    internal Task OutputWrites => _outputWrites;

    internal Task<T> QueueOutputJobForTests<T>(Func<T> job) => InOutputQueue(job);

    internal Task SaveRunOutputForTests() => SaveRunOutput();

    private Task<T> InOutputQueue<T>(Func<T> write)
    {
        var next = _outputWrites.ContinueWith(_ => write(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        _outputWrites = next;
        return next;
    }

    internal ITabSpillStore? SpillStore { get; set; }

    internal bool IsContentReleased => _contentReleased;

    /// <summary>
    /// What is to be kept is taken on the UI thread, written to disk off it (a large output, or a slow disk, must not freeze the window), and
    /// the tab lets go of its text only if it is still one to give up when that is done: edited, run, saved or looked at meanwhile, it keeps
    /// what it has. A write that failed also leaves the text where it is.
    /// </summary>
    internal async Task<bool> TryReleaseContentAsync(Func<bool> stillWanted)
    {
        if (_disposed || _contentReleased || HasRunningProcess || SpillStore is not { } store) return false;
        var editor = State.Editor;
        var document = editor.Document;
        var version = document.Version;
        var text = editor.Text;
        var viewState = CaptureViewState();
        var output = CaptureOutput();
        // The folder of the output is the one the index has now, as it is where a run or Save As would write it.
        FollowTheFolderOfTheIndex();
        var generation = Volatile.Read(ref _outputVersion);
        var folder = State.OutputDirectory;
        var id = _vm.RecoveryId;

        var written = await InOutputQueue(() =>
        {
            // The output has changed since it was taken (a run ended, the output was cleared): this release is out of date.
            if (Volatile.Read(ref _outputVersion) != generation) return false;
            // The output of a run waits in the folder of that run; only an output without one goes to the spill.
            var keptInFolder = folder is not null && SaveRunOutputOf(folder, output);
            return store.TryWrite(id, new ReleasedContent(text, viewState, keptInFolder ? null : output));
        });
        if (!written || _disposed || _contentReleased || HasRunningProcess || !stillWanted()
            || Volatile.Read(ref _outputVersion) != generation
            || !ReferenceEquals(editor.Document, document) || document.Version.CompareAge(version) != 0)
            return false;
        ReleaseNow(editor);
        return true;
    }

    private void ReleaseNow(Invoklet.Avalonia.CodeTextEditor.CodeTextEditor editor)
    {
        var dirty = _vm.IsDirty;
        // Set first: the IntelliSense service must not be told the script became empty.
        _contentReleased = true;
        // A new document frees the text, its undo history and the highlighting tokens of every line.
        editor.Document = new TextDocument();
        editor.IsReadOnly = true;
        State.Output.Clear();
        State.DumpContainerBlocks.Clear();
        _vm.IsDirty = dirty;
    }

    /// <summary>
    /// Brings the text and the output back. The file is read off the UI thread, and the editor stays
    /// invisible until it is filled, so neither a freeze nor an empty editor is seen. Asked again while it runs,
    /// it is the same restore.
    /// </summary>
    internal Task<bool> TryRestoreContentAsync()
    {
        if (_disposed || !_contentReleased) return Task.FromResult(true);
        if (_restoring is { } running) return running;
        var restoring = RestoreContentAsync();
        if (!restoring.IsCompleted) _restoring = restoring;
        return restoring;
    }

    private async Task<bool> RestoreContentAsync()
    {
        var editor = State.Editor;
        editor.Opacity = 0;
        try
        {
            var store = SpillStore;
            var id = _vm.RecoveryId;
            var path = _vm.FilePath;
            var outputFolder = State.OutputDirectory;
            // What a saved tab needs besides its text is read with it, off the UI thread: its file (a slow or network disk) and the output
            // of its last run (large). A tab with unsaved edits needs neither its file nor, if it was spilled, the output's folder.
            var looksSaved = !_vm.IsDirty && path is not null;
            var read = store is null ? null : await Task.Run(() =>
            {
                var spilled = store.TryRead(id);
                if (spilled is null) return null;
                var output = spilled.Output ?? (outputFolder is not null ? RunOutputFile.TryLoad(outputFolder) : null);
                return new RestoredContent(spilled, output, looksSaved ? TryReadBytes(path!) : null);
            });
            if (_disposed) return false;
            if (!_contentReleased) return true;
            if (read is null) return false;
            var content = read.Spilled;

            var dirty = _vm.IsDirty;
            // A script that was saved is read again, so a change made to its file meanwhile is not hidden.
            var saved = !dirty && FrontMatterSnapshot() == _metadataBaseline && _vm.FilePath is not null;
            // From here the handlers see the text come back, as they would see a typed one.
            _contentReleased = false;
            editor.IsReadOnly = false;
            editor.Document = new TextDocument(content.Text);
            if (saved)
                // The bytes read meanwhile are of this file, if the tab still has the path it had; else it is read now.
                KeepTextIfFileIsGone(looksSaved && _vm.FilePath == path ? ReloadFromDisk(read.FileBytes) : ReloadFromDisk());
            else
                _vm.IsDirty = dirty;

            RestoreOutput(read.Output);
            RestoreViewState(content.Editor);
            return true;
        }
        finally
        {
            _restoring = null;
            if (_contentReleased) editor.Opacity = 1; // not restored: the empty read-only editor, as the error says
        }
    }

    // What a restore reads off the UI thread: the spilled content, the output of the last run, and the bytes of the script's file.
    private sealed record RestoredContent(ReleasedContent Spilled, TabOutputState? Output, byte[]? FileBytes);

    // The file of a saved script was read again and could not give a script of this tab's language: what
    // the tab showed stays on screen, and it no longer matches a file, so it is unsaved.
    private void KeepTextIfFileIsGone(DiskReload result)
    {
        if (result == DiskReload.Reloaded) return;
        _vm.IsDirty = true;
        SetStatus(result == DiskReload.OtherLanguage
            ? "The file is now in another language; showing the text from before"
            : "The file could not be read; showing the text from before", "Invoklet.Status.ErrorBrush");
        UpdateOutputUi();
    }

    // The output of the last run, beside the library it ran: asked to be written when a run ends, and when the content of the tab is released.
    // What is taken now is what is written, in its turn in the queue of the writes; the window does not wait for the disk.
    private Task SaveRunOutput()
    {
        FollowTheFolderOfTheIndex();
        if (State.OutputDirectory is not { } folder) return Task.CompletedTask;
        var output = CaptureOutput();
        return InOutputQueue(() => SaveRunOutputOf(folder, output));
    }

    // Another running Invoklet may have entered a folder for this script in the shared index while this tab was given one of its own (the index was
    // locked then): the tab goes over to the index's, so that opening the script again finds its output. What the tab shows is written there.
    private void FollowTheFolderOfTheIndex()
    {
        if (_vm.FilePath is not { } path || State.OutputDirectory is not { } current) return;
        _buildCache.Touch(path); // reads the index again
        if (_buildCache.EnsureOutputDirectory(path, EnsureWorkDirectory()) is not { } shared || FilePaths.Comparer.Equals(shared, current)) return;
        State.OutputDirectory = shared;
        Interlocked.Increment(ref _outputVersion); // a release that is writing to the other folder is out of date
    }

    // The tab moved to another file (Save As, a rename): its runs are built, and their output kept, in the
    // folder of that file from now on. The folder of the old file keeps the output of the runs it saw, so
    // opening that file again does not show the output of runs of another one.
    private void RebindRunFolder(string newPath)
    {
        if (_buildCache.EnsureOutputDirectory(newPath, EnsureWorkDirectory()) is not { } folder) return;
        var old = State.OutputDirectory;
        State.OutputDirectory = folder;
        Interlocked.Increment(ref _outputVersion); // a release that is writing to the old folder is out of date
        if (!_contentReleased)
            _ = SaveRunOutput(); // what the tab shows now belongs to the new file
        else if (old is not null)
            _ = InOutputQueue(() => RunOutputFile.TryLoad(old) is { } waiting && SaveRunOutputOf(folder, waiting)); // the output is still waiting in the old folder: it moves along
    }

    // True when the output is in the folder now. Safe off the UI thread: it touches the file and the logger only.
    private bool SaveRunOutputOf(string folder, TabOutputState output)
    {
        try
        {
            RunOutputFile.Save(folder, output);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _runLogger.LogWarning("Could not keep the output of a run ({ErrorType})", ex.GetType().Name);
            return false;
        }
    }

    // Shows the output of the last run the script's folder holds, for a tab opened again.
    private void LoadRunOutput()
    {
        if (State.Output.Content.Length > 0 || State.OutputDirectory is not { } folder) return;
        if (RunOutputFile.TryLoad(folder) is { } output) RestoreOutput(output);
    }

    private EditorViewState CaptureViewState()
    {
        var editor = State.Editor;
        return new EditorViewState(editor.CaretOffset, editor.SelectionStart, editor.SelectionLength, EditorTopLine());
    }

    private void RestoreViewState(EditorViewState state)
    {
        var editor = State.Editor;
        var length = editor.Document.TextLength;
        editor.CaretOffset = Math.Min(state.CaretOffset, length);
        var start = Math.Min(state.SelectionStart, length);
        if (state.SelectionLength > 0) editor.Select(start, Math.Min(state.SelectionLength, length - start));

        // The editor may have no size yet, and a line can only be scrolled to once it has one. It stays
        // invisible until then, so the text is never seen at the top and then jumping.
        editor.Opacity = 0;
        Dispatcher.Post(() =>
        {
            if (_disposed) return;
            editor.UpdateLayout();
            ScrollEditorToLine(state.TopLine);
            editor.Opacity = 1;
        }, DispatcherPriority.Loaded);
    }

    private TabOutputState CaptureOutput()
        => new(State.Output.Content, State.HadRunError, new Dictionary<string, string>(State.DumpContainerBlocks));

    private void RestoreOutput(TabOutputState? output)
    {
        if (output is null) return;
        State.Output.Clear();
        State.Output.Append(output.Content);
        State.HadRunError = output.HadRunError;
        State.DumpContainerBlocks.Clear();
        foreach (var (id, block) in output.DumpContainerBlocks) State.DumpContainerBlocks[id] = block;
    }

    // The text and the caret for the session checkpoint: while the content is on disk, from there. A
    // checkpoint must never take the empty editor for the script, so a spill that cannot be read fails it.
    private (string Text, int Caret, int SelectionStart, int SelectionLength) TextForRecovery()
    {
        var editor = State.Editor;
        if (!_contentReleased) return (editor.Text, editor.CaretOffset, editor.SelectionStart, editor.SelectionLength);
        if (SpillStore?.TryRead(_vm.RecoveryId) is not { } content)
            throw new InvalidOperationException("The text of a tab that is not loaded could not be read.");
        return (content.Text, content.Editor.CaretOffset, content.Editor.SelectionStart, content.Editor.SelectionLength);
    }
}
