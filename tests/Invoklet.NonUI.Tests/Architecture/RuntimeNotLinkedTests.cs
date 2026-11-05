using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Invoklet.NonUI.Tests.Architecture;

// Invoklet.Runtime is what a user's script links (MIT, free of Invoklet's own code): Invoklet builds it and puts it beside the app for the
// scripts to compile against, but no assembly of Invoklet links it.
public class RuntimeNotLinkedTests
{
    internal const string Runtime = "Invoklet.Runtime";

    [Fact]
    public void NoInvokletAssembly_Should_LinkTheRuntime()
    {
        var assemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "Invoklet*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) is var name && name != Runtime && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(assemblies, path => Path.GetFileName(path) == "Invoklet.dll");

        Assert.Empty(assemblies.Where(LinksTheRuntime).Select(Path.GetFileName));
    }

    internal static bool LinksTheRuntime(string path)
    {
        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == Runtime);
    }
}
