using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp.Compiler;
using Invoklet.Languages.CSharp.IntelliSense.Roslyn;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using ExecutionRequest = Invoklet.Contracts.Models.ExecutionRequest;

namespace Invoklet.Languages.CSharp.IntelliSense.ScriptDocument;

/// <summary>The assemblies the service was created with: all of them, the framework's, and the rest.</summary>
internal sealed record ReferenceSet(IReadOnlyList<string> All, IReadOnlyList<string> DefaultFramework, IReadOnlyList<string> Other)
{
    /// <param name="referencePaths">The framework's assemblies, and others outside their folder.</param>
    /// <param name="additionalReferencePaths">Assemblies kept whatever the script's target framework, such as Invoklet.Runtime.</param>
    public static ReferenceSet Classify(IEnumerable<string> referencePaths, IEnumerable<string>? additionalReferencePaths)
    {
        var all = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additional = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in referencePaths)
            if (File.Exists(path) && seen.Add(path))
                all.Add(path);
        foreach (var path in additionalReferencePaths ?? [])
            if (File.Exists(path) && additional.Add(path) && seen.Add(path))
                all.Add(path);
        var frameworkDirectory = all
            .Where(p => !additional.Contains(p)
                        && string.Equals(Path.GetFileName(p), "System.Runtime.dll", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetDirectoryName).FirstOrDefault();
        List<string> framework = [], other = [];
        foreach (var path in all)
            (frameworkDirectory is not null && !additional.Contains(path) && Path.GetDirectoryName(path) == frameworkDirectory
                ? framework : other).Add(path);
        return new(all, framework, other);
    }
}

/// <summary>
/// What the analysis project is compiled against besides the script's own text: the target framework's references and source
/// generators, the script's assemblies, its packages, the typed <c>Arguments</c> and the generated connection documents. Every change
/// is made to the <see cref="ScriptDocumentSession"/> under its lock and marks the context as changed there.
/// </summary>
internal sealed class ScriptContext
{
    private readonly ScriptDocumentSession _session;
    private readonly ScriptDocumentLayout _layout;
    private readonly ILogger _logger;

    // The references the service was created with (the framework's, Invoklet.Runtime): package updates select from these again, so a
    // framework assembly a package displaced comes back with it. The framework part is replaced by the script target's reference
    // assemblies when its targeting pack is found, as the build compiles against it.
    private readonly IReferenceResolver _resolver;
    private readonly IReadOnlyList<string> _defaultFrameworkPaths;
    private readonly IReadOnlyList<string> _otherBaseReferencePaths;
    private IReadOnlyList<string> _frameworkPaths;
    // The target pack's source generators ([GeneratedRegex], System.Text.Json, [LibraryImport]), which the build runs too. The workspace
    // runs them while compiling for analysis, off the UI thread.
    private IReadOnlyList<string> _generatorPaths = [];
    // The script's own .dll references with the same-folder dependencies the build adds.
    private volatile IReadOnlyList<string> _userReferencePaths = [];
    private IReadOnlyList<FileStamp> _userReferenceStamps = [];
    // Replaced as a whole, never changed in place: completion and navigation read them without the lock.
    private volatile IReadOnlyList<MetadataReference> _nugetRefs = [];
    private IReadOnlyList<string> _nugetPaths = [];
    private IReadOnlyList<MdRunArgumentContractMember> _argumentContract = [];
    private volatile List<KeyValuePair<string, (DocumentId Id, string Text)>> _connectionFiles = [];

    // Test-only: called when an execution context request has done its slow lookups and is about to be applied, so a test can hold an
    // older request there while a newer one overtakes it.
    internal Func<ExecutionRequest, Task>? ExecutionContextResolvedForTests { get; set; }

    // Shared across every ScriptContext instance (one per tab, see EditorTabsControl.
    // CreateIntelliSenseAsync) — MetadataReferences are immutable and safe to share across unrelated
    // workspaces/compilations, so without this every new tab redid the PE-header + XML-doc parsing
    // for the same ~167 default BCL reference assemblies from scratch.
    internal static readonly ConcurrentDictionary<string, CachedReference> ReferenceCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A reference and the file it was read from; a file that has changed since (a library built again in the same place) is read again.</summary>
    internal sealed record CachedReference(FileStamp Stamp, MetadataReference Reference);

    // See ReferenceDocumentation — without this, GetDocumentationCommentXml() comes back empty for
    // every metadata symbol (BCL, NuGet), so the doc-comment Summary was silently null for every
    // completion/signature-help/quick-info item except symbols the script itself defines.
    internal static MetadataReference CreateReference(string path)
    {
        var stamp = FileStamp.Of(path);
        if (ReferenceCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Reference;
        var reference = MetadataReference.CreateFromFile(path, documentation: ReferenceDocumentation.Find(path));
        ReferenceCache[path] = new CachedReference(stamp, reference);
        return reference;
    }


    /// <param name="resolver">Finds the assemblies that packages and a target framework stand for; the file system's unless given (a test gives its own).</param>
    public ScriptContext(ScriptDocumentSession session, ScriptDocumentLayout layout, ReferenceSet references, ILogger logger, IReferenceResolver? resolver = null)
    {
        _resolver = resolver ?? new FileSystemReferenceResolver(references.DefaultFramework, logger);
        _session = session;
        _layout = layout;
        _logger = logger;
        _defaultFrameworkPaths = references.DefaultFramework;
        _otherBaseReferencePaths = references.Other;
        _frameworkPaths = references.DefaultFramework;
    }

    internal IReadOnlyList<MdRunArgumentContractMember> ArgumentContract => _argumentContract;

    internal IReadOnlyList<string> PackageReferencePaths
    {
        get { lock (_session.SyncLock) return _nugetPaths; }
    }

    /// <summary>Whether a reference is one the script added (a package or an assembly of its own), not the framework's.</summary>
    internal bool IsAddedReference(MetadataReference reference) =>
        _nugetRefs.Contains(reference)
        || (reference is PortableExecutableReference { FilePath: { } path }
            && _userReferencePaths.Contains(path, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the code needs the connection schema types as source, not from the build's cached contract assembly.</summary>
    internal void RequireContractSources(bool needed)
    {
        lock (_session.SyncLock)
        {
            if (needed == _contractSourcesNeeded) return;
            _contractSourcesNeeded = needed;
            ApplyConnectionRepresentation();
        }
    }

    /// <summary>Updates the typed <c>Arguments</c> members offered by completion.</summary>
    private (DocumentId Id, string Text)? _argumentSource;

    internal void UpdateArgumentContract(MdRunArgumentContract? contract)
    {
        var members = contract?.Members ?? [];
        lock (_session.SyncLock)
        {
            var source = contract is { Members.Count: > 0 } ? contract.GenerateCSharpFacade() : "";
            if (_argumentContract.SequenceEqual(members) && (_argumentSource?.Text ?? "") == source) return;

            var project = _session.Workspace.CurrentSolution.GetProject(_session.DocumentId.ProjectId);
            if (project is null) return;
            var solution = project.Solution;
            if (_argumentSource is { } old) solution = solution.RemoveDocument(old.Id);
            (DocumentId Id, string Text)? next = null;
            if (source.Length > 0)
            {
                var id = DocumentId.CreateNewId(_session.DocumentId.ProjectId, "Arguments.g.cs");
                solution = solution.AddDocument(id, "Arguments.g.cs", SourceText.From(GeneratedHeader + source, Encoding.UTF8));
                next = (id, source);
            }
            if (!_session.Workspace.TryApplyChanges(solution)) return;
            _argumentContract = members;
            _argumentSource = next;
            _session.InvalidateBaseline();
            _session.ContextChanged();
        }
    }


    // The build's own reading of the request (see DotnetPackageReferenceContext). Without an SDK
    // yet, the runtime IntelliSense itself runs on.
    private string? _connectionContractReference;
    private GeneratedConnections? _requestedConnections;
    private string? _requestedContractAssembly;
    private bool _contractSourcesNeeded;
    private int _contextRequests;
    private int _appliedContextRequest; // under _session.SyncLock

    private void ApplyConnectionRepresentation()
    {
        var contract = _contractSourcesNeeded ? null : _requestedContractAssembly;
        UpdateConnections(_requestedConnections is { Source.Length: > 0 } connections
            ? [new GeneratedSourceFile("Connections.g.cs", connections.Source), .. contract is null ? connections.Files : []]
            : [], contract);
    }

    // A request cancelled before it is applied changes nothing: the slow parts (reading the NuGet
    // assets, the targeting pack, the contract assembly lookup) run without the workspace lock, and
    // a caller that has since asked again must not have its newer context overwritten when this one
    // finally finishes. Nothing of a request (references, generators, connections) is applied before
    // the check under the lock that it is still the latest.
    //
    // Requests also supersede each other by the order they were made in: one that finishes after a
    // later request has been applied is dropped, whether or not its caller cancelled it.
    internal async Task<bool> ApplyExecutionContextAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Increment(ref _contextRequests);
        var packages = request.Packages.ToDictionary(p => p.Key, p => new Package(p.Value));
        var context = DotnetPackageReferenceContext.For(request);
        var resolution = _resolver.Resolve(packages,
            context ?? new PackageReferenceContext($"net{Environment.Version.Major}.{Environment.Version.Minor}", null, null));
        cancellationToken.ThrowIfCancellationRequested();

        string? contract = null;
        if (request.Connections is { Files.Count: > 0 } generated && context?.OutputDirectory is { } output)
        {
            var (framework, packagePaths) = RoslynScriptCompiler.ResolveConflicts(resolution.FrameworkPaths, resolution.NewPaths);
            try
            {
                contract = ConnectionContractAssemblyCache.Find(RoslynScriptCompiler.ObjDirectory(output), generated.Files,
                    framework.Concat(packagePaths), context.TargetFramework + ";" + (request.AllowUnsafe ? "unsafe" : ""));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "No cached connection contract available");
            }
        }
        if (ExecutionContextResolvedForTests is { } resolved) await resolved(request);
        lock (_session.SyncLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sequence < _appliedContextRequest) return false;
            _appliedContextRequest = sequence;
            var previousVersion = _session.ContextVersion;
            // One request is applied whole or not at all: the references, the setting and the connections are of the same request.
            CommitPackageReferences(resolution);
            UpdateAllowUnsafe(request.AllowUnsafe);
            _requestedConnections = request.Connections;
            _requestedContractAssembly = contract;
            ApplyConnectionRepresentation();
            return !_session.IsCurrent(previousVersion);
        }
    }

    // A source location in the script's own document, not in the generated Connections document,
    // whose offsets mean nothing in the editor.
    internal bool IsInScript(Location location) =>
        location.IsInSource && location.SourceTree is { } tree && _session.Workspace.CurrentSolution.GetDocumentId(tree) == _session.DocumentId;

    // A symbol declared in one of the generated connection documents (UpdateConnections) rather
    // than the script itself or a compiled metadata assembly -- the state every gRPC/HTTP-contract
    // message and client class is in until the first successful build produces a cached contract
    // assembly (ConnectionContractAssemblyCache). Neither IsInScript nor a metadata reference
    // matches a symbol there, so without this, TryGetDefinitionAsync falls through and F12 is a
    // silent no-op.
    internal (string Text, int Line)? GeneratedDocumentFor(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is not { } tree) continue;
            var id = _session.Workspace.CurrentSolution.GetDocumentId(tree);
            if (id is null) continue;
            foreach (var (_, (docId, text)) in _connectionFiles)
                if (docId == id)
                    // 0-based in the document, which has GeneratedHeader's one line above the text: 1-based in the text.
                    return (text, location.GetLineSpan().StartLinePosition.Line);
            // The typed arguments of the script's front matter.
            if (_argumentSource is { } arguments && arguments.Id == id)
                return (arguments.Text, location.GetLineSpan().StartLinePosition.Line);
        }
        return null;
    }

    private const string GeneratedHeader = "// <auto-generated/>\n";

    // The small Connections wrapper stays source. Schema types use the build's cached assembly
    // when available; before the first build (or for partial extensions) they remain source documents.
    private void UpdateConnections(IReadOnlyList<GeneratedSourceFile> files, string? contract)
    {
        lock (_session.SyncLock)
        {
            var project = _session.Workspace.CurrentSolution.GetProject(_session.DocumentId.ProjectId)!;
            var filesChanged = !files.Select(f => (f.Name, f.Text)).SequenceEqual(_connectionFiles.Select(f => (f.Key, f.Value.Text)));
            var referencesChanged = contract != _connectionContractReference
                || contract is not null && !project.MetadataReferences.Any(r => r.Display == contract);
            if (!filesChanged && !referencesChanged) return;
            var solution = project.Solution;
            if (referencesChanged)
            {
                var references = project.MetadataReferences.Where(r => r.Display != _connectionContractReference).ToList();
                if (contract is not null) references.Add(CreateReference(contract));
                solution = project.WithMetadataReferences(references).Solution;
            }
            var added = _connectionFiles;
            if (filesChanged)
            {
                foreach (var id in _connectionFiles.Select(f => f.Value.Id)) solution = solution.RemoveDocument(id);
                added = [];
                foreach (var file in files)
                {
                    var id = DocumentId.CreateNewId(_session.DocumentId.ProjectId, file.Name);
                    solution = solution.AddDocument(id, file.Name, SourceText.From(GeneratedHeader + file.Text, Encoding.UTF8));
                    added.Add(KeyValuePair.Create(file.Name, (id, file.Text)));
                }
            }
            // A single solution change: analysis must never see both source and metadata definitions.
            if (!_session.Workspace.TryApplyChanges(solution)) return;
            _connectionFiles = added;
            _connectionContractReference = contract;
            _session.ContextChanged();
        }
    }

    // The same /unsafe setting the build uses, so the editor neither flags allowed unsafe code (CS0227)
    // nor hides that a script without the setting won't compile.
    private void UpdateAllowUnsafe(bool allowUnsafe)
    {
        lock (_session.SyncLock)
        {
            var project = _session.Workspace.CurrentSolution.GetProject(_session.DocumentId.ProjectId)!;
            var options = (CSharpCompilationOptions)project.CompilationOptions!;
            if (options.AllowUnsafe == allowUnsafe) return;
            if (!_session.Workspace.TryApplyChanges(project.WithCompilationOptions(options.WithAllowUnsafe(allowUnsafe)).Solution)) return;
            _session.ContextChanged(); // force re-sync on next analysis pass
        }
    }

    // Without a build context: the runtime IntelliSense itself runs on, the user's NuGet settings.
    internal void UpdatePackageReferences(IReadOnlyDictionary<string, Package> packages)
        => UpdatePackageReferences(packages,
            new PackageReferenceContext($"net{Environment.Version.Major}.{Environment.Version.Minor}", null, null));

    // The same assemblies a build compiles against: the target framework's reference assemblies, and the
    // last restore's selection while it still matches the packages (resolved versions, transitive
    // packages, the target framework's compile assets), else the best local match by NuGet's version
    // rules — never a restore or a targeting pack download, which would need the network.
    internal void UpdatePackageReferences(IReadOnlyDictionary<string, Package> packages, PackageReferenceContext context)
        => ApplyPackageReferences(packages, context, CancellationToken.None);

    private void ApplyPackageReferences(IReadOnlyDictionary<string, Package> packages, PackageReferenceContext context,
        CancellationToken cancellationToken)
    {
        var resolution = _resolver.Resolve(packages, context);

        // The same workspace lock as SyncDocument: a background analysis pass applies its own changes
        // under it, and both sides must see one consistent "current" solution.
        lock (_session.SyncLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitPackageReferences(resolution);
        }
    }

    // Called with the workspace lock held.
    private void CommitPackageReferences(PackageResolution resolution)
    {
        var (newPaths, frameworkPaths, generatorPaths, userReferencePaths, userReferenceStamps, parseOptions) = resolution;
        if (newPaths.SequenceEqual(_nugetPaths, StringComparer.OrdinalIgnoreCase)
            && frameworkPaths.SequenceEqual(_frameworkPaths, StringComparer.OrdinalIgnoreCase)
            && parseOptions.Equals(_layout.ParseOptions)
            && generatorPaths.SequenceEqual(_generatorPaths, StringComparer.OrdinalIgnoreCase)
            && userReferencePaths.SequenceEqual(_userReferencePaths, StringComparer.OrdinalIgnoreCase)
            // The same path can hold another library since: a script's own DLL that was built again.
            && userReferenceStamps.SequenceEqual(_userReferenceStamps)) return;

        // Rebuilt from the base references every time, so stale package ones (a removed package, an
        // older resolved version) go away. A package assembly the framework also has is resolved as
        // the build resolves it: referencing both would make every one of its types ambiguous
        // (CS0433) in the editor while the build compiles.
        var (basePaths, packagePaths) = RoslynScriptCompiler.ResolveConflicts(
            frameworkPaths.Concat(_otherBaseReferencePaths), newPaths);
        // In the build's order: the framework (and Invoklet.Runtime), the script's own assemblies, packages.
        var baseRefs = basePaths.Concat(userReferencePaths).Select(p => (MetadataReference)CreateReference(p)).ToList();
        var newRefs = packagePaths.Select(p => (MetadataReference)CreateReference(p)).ToList();

        // Replaced as a whole: another target's generators never linger next to the new ones.
        var generatorRefs = generatorPaths.Select(p => (AnalyzerReference)new GeneratorReference(p, _logger)).ToList();

        var project = _session.Workspace.CurrentSolution.GetProject(_session.DocumentId.ProjectId)!;

        if (!_session.Workspace.TryApplyChanges(project.WithMetadataReferences(baseRefs.Concat(newRefs).ToList())
                .WithParseOptions(parseOptions).WithAnalyzerReferences(generatorRefs).Solution))
            return; // the next update tries again: _nugetPaths still describes the workspace

        _nugetRefs = newRefs;
        _nugetPaths = newPaths;
        _frameworkPaths = frameworkPaths;
        _layout.ParseOptions = parseOptions;
        _generatorPaths = generatorPaths;
        _userReferencePaths = userReferencePaths;
        _userReferenceStamps = userReferenceStamps;
        // force re-sync on next analysis pass
        _session.ContextChanged();
    }

    // Exposes only the source generators of an assembly: the analyzers and code fixes that ship next to
    // them are not part of the build's diagnostics either. Loaded when the workspace first asks, while
    // compiling for analysis — not on the thread that changes the target. The path is the identity, so
    // an unchanged generator is recognized by the workspace across updates.
    private sealed class GeneratorReference(string path, ILogger logger) : AnalyzerReference
    {
        public override string FullPath => path;
        public override string Display => Path.GetFileNameWithoutExtension(path);
        public override object Id => path;
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) =>
            language == LanguageNames.CSharp ? GetGeneratorsForAllLanguages() : [];
        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() =>
            RoslynScriptCompiler.LoadGenerators(path, LanguageNames.CSharp, logger);
    }
}
