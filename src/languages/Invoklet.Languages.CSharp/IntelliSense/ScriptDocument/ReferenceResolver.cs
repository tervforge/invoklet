using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp.Compiler;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Compiler;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;

namespace Invoklet.Languages.CSharp.IntelliSense.ScriptDocument;

/// <summary>
/// What tells that a file is still the one that was read: its size and the time it was last written. A file replaced by another of the same
/// size within the clock's resolution is not noticed.
/// </summary>
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    public static FileStamp Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : default;
    }
}

/// <summary>What a package update selects: the assemblies of the packages, of the target framework and of the script, the source generators, the language version.</summary>
internal sealed record PackageResolution(IReadOnlyList<string> NewPaths, IReadOnlyList<string> FrameworkPaths, IReadOnlyList<string> GeneratorPaths,
    IReadOnlyList<string> UserReferencePaths, IReadOnlyList<FileStamp> UserReferenceStamps, CSharpParseOptions ParseOptions);

/// <summary>
/// Finds, for the packages and the target of a script, the assemblies the editor compiles against. It is the one part of the editor's context that
/// looks at the disk (the NuGet folders, the targeting packs, the files beside the script's own libraries); what is done with the answer (the order of
/// requests, the conflicts between a package and the framework, the workspace) is the same whatever the answer is, and is tested with a resolver of its own.
/// </summary>
internal interface IReferenceResolver
{
    /// <summary>Called without the workspace lock: it may read files and take time.</summary>
    PackageResolution Resolve(IReadOnlyDictionary<string, Package> packages, PackageReferenceContext context);
}

/// <summary>The real thing: the installed targeting pack and the NuGet folders, never a restore or a download.</summary>
internal sealed class FileSystemReferenceResolver(IReadOnlyList<string> defaultFrameworkPaths, ILogger logger) : IReferenceResolver
{
    // The same assemblies a build compiles against: the target framework's reference assemblies, and the last restore's selection while it
    // still matches the packages (resolved versions, transitive packages, the target framework's compile assets), else the best local match
    // by NuGet's version rules.
    public PackageResolution Resolve(IReadOnlyDictionary<string, Package> packages, PackageReferenceContext context)
    {
        var (frameworkPaths, generatorPaths) = TargetFrameworkReferences(context);
        var userReferencePaths = RoslynScriptCompiler.WithSameFolderDependencies(context.AssemblyReferences ?? []);
        var parseOptions = TargetFramework.TryParse(context.TargetFramework, out var major, out var minor)
            ? CSharpScriptCompiler.ParseOptions(major, minor)
            : CSharpScriptCompiler.ParseOptions(Environment.Version.Major, Environment.Version.Minor);
        var newPaths = NuGetRestorer.CompileReferencesWithoutRestore(
                packages.ToDictionary(p => p.Key, p => p.Value.Version, StringComparer.OrdinalIgnoreCase),
                context.TargetFramework,
                context.OutputDirectory is { } output ? RoslynScriptCompiler.ObjDirectory(output) : null,
                context.NuGetConfigFile)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageResolution(newPaths, frameworkPaths, generatorPaths, userReferencePaths, [.. userReferencePaths.Select(FileStamp.Of)], parseOptions);
    }

    // The targeting pack the build would use for the target, among the installed and already downloaded
    // ones: its reference assemblies and C# source generators. Without one (or with a malformed NuGet
    // configuration), the references the service was created with and no generators, as before.
    private (IReadOnlyList<string> References, IReadOnlyList<string> Generators) TargetFrameworkReferences(PackageReferenceContext context)
    {
        try
        {
            var settings = NuGetRestorer.LoadSettings(context.NuGetConfigFile,
                context.OutputDirectory is { } output ? RoslynScriptCompiler.ObjDirectory(output) : Path.GetTempPath());
            var pack = TargetingPack.Find(context.TargetFramework, context.DotnetExecutable,
                SettingsUtility.GetGlobalPackagesFolder(settings));
            if (pack is not null)
                return (pack.ReferenceAssemblies.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                    pack.GeneratorAssemblies("cs").Order(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NuGetConfigurationException)
        {
            logger.LogDebug(ex, "No targeting pack for {TargetFramework}", context.TargetFramework);
        }
        return (defaultFrameworkPaths, []);
    }
}
