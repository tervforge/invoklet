using Invoklet.Contracts.IntelliSense;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp.Compiler;
using Invoklet.Languages.CSharp.IntelliSense.ScriptDocument;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;

// Answers a package update without looking at the disk: the framework the service was made with, the assemblies the test names for packages
// and for the script's own libraries, and no generators. What a real search finds (NuGet folders, targeting packs) is tested where it is searched for.
internal sealed class FixedReferenceResolver(
    IReadOnlyList<string> framework,
    Func<IReadOnlyDictionary<string, Package>, IReadOnlyList<string>>? packagePaths = null,
    IReadOnlyList<string>? userPaths = null) : IReferenceResolver
{
    public PackageResolution Resolve(IReadOnlyDictionary<string, Package> packages, PackageReferenceContext context)
    {
        var user = userPaths ?? [];
        return new PackageResolution(packagePaths?.Invoke(packages) ?? [], framework, [], user, [.. user.Select(_ => default(FileStamp))],
            CSharpScriptCompiler.ParseOptions(Environment.Version.Major, Environment.Version.Minor));
    }
}
