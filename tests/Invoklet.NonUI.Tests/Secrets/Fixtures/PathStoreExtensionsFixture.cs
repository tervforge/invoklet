using Invoklet.Secrets;
using Moq;

namespace Invoklet.NonUI.Tests.Secrets;

// A Path Store in memory: what it holds, at which version, and what it is saved with.
internal sealed class PathStoreExtensionsFixture
{
    public static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;

    public Mock<IPathStore> Store { get; } = new();

    public List<IReadOnlyList<NamedPath>> Saved { get; } = [];

    public PathStoreExtensionsFixture()
    {
        Holds();
        Store.Setup(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>()))
            .Callback((IReadOnlyList<NamedPath> paths, string? _) => Saved.Add(paths))
            .Returns("v2");
    }

    public static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Holds(params NamedPath[] paths) =>
        Store.Setup(s => s.LoadSnapshot()).Returns(new PathStoreSnapshot(paths, paths.Length == 0 ? null : "v1"));

    /// <summary>The first save finds the store changed by another window.</summary>
    public void ChangesUnderTheFirstSave() =>
        Store.SetupSequence(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>()))
            .Throws(new PathStoreConflictException())
            .Returns("v3");

    public IPathStore CreateSut() => Store.Object;
}
