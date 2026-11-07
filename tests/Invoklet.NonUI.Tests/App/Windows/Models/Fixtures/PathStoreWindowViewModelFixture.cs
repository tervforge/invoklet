using Invoklet.Secrets;
using Invoklet.Windows.Models;
using Moq;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// The Path Store window over a store in memory; which full paths have something at them is the fixture's say, not the disk's.
internal sealed class PathStoreWindowViewModelFixture
{
    public static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;

    public Mock<IPathStore> Store { get; } = new();

    public HashSet<string> Existing { get; } = [];

    public List<IReadOnlyList<NamedPath>> Saved { get; } = [];

    public PathStoreWindowViewModelFixture()
    {
        Holds();
        Store.SetupGet(s => s.FilePath).Returns(At("paths.json"));
        Store.Setup(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>()))
            .Callback((IReadOnlyList<NamedPath> paths, string? _) => Saved.Add(paths))
            .Returns("v2");
    }

    public static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Holds(params NamedPath[] paths) =>
        Store.Setup(s => s.LoadSnapshot()).Returns(new PathStoreSnapshot(paths, paths.Length == 0 ? null : "v1"));

    public void IsDamaged() => Store.Setup(s => s.LoadSnapshot()).Throws(new InvalidDataException("The path store file is damaged."));

    public void ChangesInAnotherWindow() =>
        Store.Setup(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), It.IsAny<string?>())).Throws(new PathStoreConflictException());

    public PathStoreWindowViewModel CreateSut() => new(Store.Object, Existing.Contains);
}
