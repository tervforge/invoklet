using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using Invoklet.Secrets;
using Invoklet.Shared;
using ReactiveUI;

namespace Invoklet.Windows.Models;

// One editable row of the Path Store window; the editor under the list binds straight to it.
public sealed class PathItemViewModel : ReactiveObject
{
    private readonly Func<string, bool> _exists;

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value ?? "");
    }

    // As typed: ~ and the rest are resolved when the store is saved.
    public string Path
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value ?? "");
            this.RaisePropertyChanged(nameof(Resolved));
            this.RaisePropertyChanged(nameof(IsMissing));
        }
    }

    /// <summary>The full path a script gets; null while the path isn't valid.</summary>
    public string? Resolved
    {
        get
        {
            try { return PathStoreRules.Resolve(Path); }
            catch (ArgumentException) { return null; }
        }
    }

    /// <summary>A valid path with nothing there now: saved all the same, as a drive may be mounted later.</summary>
    public bool IsMissing => Resolved is { } path && !_exists(path);

    internal PathItemViewModel(NamedPath path, Func<string, bool> exists)
    {
        _exists = exists;
        Name = path.Name;
        Path = path.Path;
    }
}

// Tools → Path Store: the user's named paths, which scripts read with Util.GetPath("name"). Edits stay in the window until Save writes
// the whole store; Cancel drops them. A store that can't be read is reported and never overwritten from here.
public sealed class PathStoreWindowViewModel : ViewModelBase
{
    private const string NewPathName = "path";

    private readonly IPathStore _store;
    private readonly Func<string, bool> _exists;
    private string? _version;
    private NamedPath[] _saved = [];

    public ObservableCollection<PathItemViewModel> Paths { get; } = [];

    public PathItemViewModel? SelectedPath
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedPath is not null;
    public bool IsEmpty => LoadError is null && Paths.Count == 0;

    // Why the store couldn't be read; while set, nothing can be saved.
    public string? LoadError { get; }

    public bool HasLoadError => LoadError is not null;

    public string? SaveError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StoreError));
            this.RaisePropertyChanged(nameof(HasStoreError));
        }
    }

    public string? StoreError => LoadError ?? SaveError;
    public bool HasStoreError => StoreError is not null;

    public string? ValidationError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasValidationError));
            this.RaisePropertyChanged(nameof(CanSave));
        }
    }

    public bool HasValidationError => ValidationError is not null;

    public bool CanSave => LoadError is null && ValidationError is null;

    public string StorePath => _store.FilePath;

    // Whether the paths differ from what was loaded or last saved; the selection doesn't count.
    public bool HasChanges { get; private set; }

    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    /// <param name="exists">Whether something is at a full path; by default a file or folder on the disk.</param>
    public PathStoreWindowViewModel(IPathStore store, Func<string, bool>? exists = null)
    {
        _store = store;
        _exists = exists ?? (path => Directory.Exists(path) || File.Exists(path));
        Paths.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(IsEmpty));
        try
        {
            var snapshot = store.LoadSnapshot();
            _version = snapshot.Version;
            foreach (var path in snapshot.Paths) Add(new PathItemViewModel(path, _exists));
            _saved = [.. snapshot.Paths];
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LoadError = $"{ex.Message} It is kept unchanged: {store.FilePath}";
        }
        SelectedPath = Paths.FirstOrDefault();

        AddCommand = ReactiveCommand.Create(AddNew, this.WhenAnyValue(x => x.HasLoadError, error => !error));
        RemoveCommand = ReactiveCommand.Create(RemoveSelected, this.WhenAnyValue(x => x.HasSelection));
        Validate();
    }

    // Writes the store, each path resolved; false (nothing written) while a row is invalid or the store unreadable.
    public bool Save()
    {
        SaveError = null;
        Validate();
        if (!CanSave) return false;
        try
        {
            NamedPath[] paths = [.. Paths.Select(p => new NamedPath(p.Name.Trim(), p.Resolved!))];
            _version = _store.Save(paths, _version);
            _saved = paths;
            foreach (var item in Paths) item.Path = item.Resolved!;
            UpdateHasChanges();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex switch
            {
                PathStoreConflictException => "The path store changed in another window. Reopen it before saving. Your edits have not been written.",
                PathStoreBusyException => "The path store is in use by another Invoklet window. Try again in a moment. Your edits are still here.",
                _ => $"Could not save the path store: {ex.Message} Your edits are still here.",
            };
            return false;
        }
    }

    private void Add(PathItemViewModel item)
    {
        item.PropertyChanged += OnItemChanged;
        Paths.Add(item);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Validate();

    private void AddNew()
    {
        var name = NewPathName;
        for (var i = 2; Paths.Any(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{NewPathName}{i}";
        var item = new PathItemViewModel(new NamedPath(name, "~"), _exists);
        Add(item);
        SelectedPath = item;
        Validate();
    }

    private void RemoveSelected()
    {
        if (SelectedPath is not { } item) return;
        var index = Paths.IndexOf(item);
        item.PropertyChanged -= OnItemChanged;
        Paths.RemoveAt(index);
        SelectedPath = Paths.Count == 0 ? null : Paths[Math.Min(index, Paths.Count - 1)];
        Validate();
    }

    private void UpdateHasChanges()
    {
        var changed = LoadError is null && !Paths.Select(p => new NamedPath(p.Name.Trim(), p.Resolved ?? p.Path)).SequenceEqual(_saved);
        if (changed == HasChanges) return;
        HasChanges = changed;
        this.RaisePropertyChanged(nameof(HasChanges));
    }

    // The store's own rules, so what the window lets through is what a script can read.
    private void Validate()
    {
        UpdateHasChanges();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Paths)
        {
            try
            {
                var name = PathStoreRules.Name(item.Name);
                if (!names.Add(name))
                {
                    ValidationError = $"Two paths are named '{name}'; names must differ (letter case aside).";
                    return;
                }
                PathStoreRules.Resolve(item.Path);
            }
            catch (ArgumentException ex)
            {
                ValidationError = ex.Message;
                return;
            }
        }
        ValidationError = null;
    }
}
