using System.Text.Json;
using Invoklet.Plugins;
using Invoklet.Plugins.Hosting;

try
{
    switch (args)
    {
        case ["stage", var source, var output]:
            Stage(Path.GetFullPath(source), Path.GetFullPath(output));
            return 0;
        case ["verify", var output]:
            Verify(Path.Combine(Path.GetFullPath(output), "plugins"));
            return 0;
        case ["refresh-after-signing", var output]:
            RefreshAfterSigning(Path.Combine(Path.GetFullPath(output), "plugins"));
            return 0;
        default:
            Console.Error.WriteLine("Usage: Invoklet.PluginPackager stage <source> <output> | verify <output> | refresh-after-signing <output>");
            return 2;
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
{
    Console.Error.WriteLine($"Plugin packaging failed: {ex.Message}");
    return 1;
}

static void Stage(string source, string output)
{
    var target = Path.Combine(output, "plugins");
    if (IsInside(source, output) || IsInside(target, source))
        throw new IOException("Plugin source and output overlap.");

    var packages = ReadPackages(source);
    var catalogue = new BundledPluginCatalog(packages.Select(package => package.Entry)).ToJson();
    if (Directory.Exists(target))
    {
        RejectLink(target);
        var cataloguePath = Path.Combine(target, BundledPluginCatalog.FileName);
        if (File.Exists(cataloguePath))
        {
            RejectLink(cataloguePath);
            if (MatchesGeneratedLayout(target, cataloguePath, catalogue, packages)) return;
        }
        else if (Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException($"Existing plugin folder has no bundled catalogue: {target}");
    }

    Directory.CreateDirectory(output);
    var temporary = Path.Combine(output, $".invoklet-plugins-{Guid.NewGuid():N}");
    var previous = temporary + "-previous";
    Directory.CreateDirectory(temporary);
    try
    {
        foreach (var package in packages)
        {
            var destination = Path.Combine(temporary, package.Role, package.Entry.Id);
            foreach (var file in Directory.EnumerateFiles(package.Directory, "*", SearchOption.AllDirectories))
            {
                var destinationFile = Path.Combine(destination, Path.GetRelativePath(package.Directory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(file, destinationFile);
            }
        }
        File.WriteAllText(Path.Combine(temporary, BundledPluginCatalog.FileName), catalogue);
        Verify(temporary);

        if (Directory.Exists(target)) Directory.Move(target, previous);
        try { Directory.Move(temporary, target); }
        catch
        {
            if (Directory.Exists(previous) && !Directory.Exists(target)) Directory.Move(previous, target);
            throw;
        }
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
    }
    finally
    {
        if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
    }
}

static bool MatchesGeneratedLayout(string target, string cataloguePath, string catalogue, IReadOnlyList<Package> packages)
{
    try
    {
        if (File.ReadAllText(cataloguePath) != catalogue) return false;
        var existing = ReadPackages(target);
        return SameLayout(packages, existing)
            && new BundledPluginCatalog(existing.Select(package => package.Entry)).ToJson() == catalogue;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
    {
        // The catalogue marks a generated layout. Stage a fresh one when its files cannot be reused.
        return false;
    }
}

static bool SameLayout(IReadOnlyList<Package> first, IReadOnlyList<Package> second) =>
    first.Select(package => (package.Entry.Id, package.Role)).SequenceEqual(second.Select(package => (package.Entry.Id, package.Role)));

static bool IsInside(string root, string path)
{
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}

static string Verify(string root)
{
    var expected = new BundledPluginCatalog(ReadPackages(root).Select(package => package.Entry)).ToJson();
    var catalogue = File.ReadAllText(Path.Combine(root, BundledPluginCatalog.FileName));
    if (catalogue != expected) throw new IOException($"Bundled catalogue does not match the plugin layout and contents in {root}.");
    return catalogue;
}

static void RefreshAfterSigning(string root)
{
    var packages = ReadPackages(root);
    var path = Path.Combine(root, BundledPluginCatalog.FileName);
    var previous = JsonSerializer.Deserialize<BundledPluginEntry[]>(File.ReadAllText(path), new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
    }) ?? throw new IOException($"Missing bundled catalogue entries in {root}.");
    if (!previous.Select(entry => (entry.Id, entry.Version)).SequenceEqual(packages.Select(package => (package.Entry.Id, package.Entry.Version))))
        throw new IOException($"Bundled plugin ids or versions changed during signing in {root}.");
    File.WriteAllText(path, new BundledPluginCatalog(packages.Select(package => package.Entry)).ToJson());
}

static IReadOnlyList<Package> ReadPackages(string root)
{
    if (!Directory.Exists(root)) return [];
    RejectLink(root);
    var source = new PluginSource(root, PluginTrust.Bundled);
    var packages = new List<Package>();
    foreach (var roleDirectory in Directory.EnumerateFileSystemEntries(root))
    {
        RejectLink(roleDirectory);
        var role = Path.GetFileName(roleDirectory);
        if (role == BundledPluginCatalog.FileName && File.Exists(roleDirectory)) continue;
        // Themes ship under plugins/themes/<id>, as the plan lays them out; every other role folder is named after the role.
        var kind = role == "themes" ? PluginRole.Theme : PluginRoles.Parse(role);
        if (kind is null || !Directory.Exists(roleDirectory)) throw new IOException($"Unknown plugin role folder: {roleDirectory}");
        foreach (var directory in Directory.EnumerateFileSystemEntries(roleDirectory))
        {
            RejectLink(directory);
            if (!Directory.Exists(directory)) throw new IOException($"Expected a plugin directory: {directory}");
            var candidate = PluginDiscovery.ReadFolder(directory, source);
            var manifest = candidate.Manifest ?? throw new IOException(string.Join(Environment.NewLine, candidate.Diagnostics.Select(diagnostic => diagnostic.Message)));
            if (manifest.Id != Path.GetFileName(directory) || !manifest.HasRole(kind.Value))
                throw new IOException($"Manifest id or role does not match the package folder: {directory}");
            if (manifest.Entry is { } entry && !File.Exists(Path.Combine(directory, entry.Assembly)))
                throw new IOException($"Missing entry assembly '{entry.Assembly}' in {directory}.");
            packages.Add(new Package(directory, role, new BundledPluginEntry(manifest.Id, manifest.Version, PluginContent.ComputeHash(directory)),
                manifest.DependsOn));
        }
    }
    // What ships together must load together: a bundled plugin can't depend on one the user has to install.
    var ids = packages.Select(package => package.Entry.Id).ToHashSet(StringComparer.Ordinal);
    foreach (var package in packages)
        foreach (var dependency in package.DependsOn)
            if (!ids.Contains(dependency))
                throw new IOException($"'{package.Entry.Id}' depends on '{dependency}', which is not among the bundled plugins.");
    return packages.OrderBy(package => package.Entry.Id, StringComparer.Ordinal).ToArray();
}

static void RejectLink(string path)
{
    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        throw new IOException($"Plugin packaging does not follow links: {path}");
}

file sealed record Package(string Directory, string Role, BundledPluginEntry Entry, IReadOnlyList<string> DependsOn);
