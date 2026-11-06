using Invoklet.Services.Preferences;
using Invoklet.Services.Setup;
using Invoklet.Services.Updates;
using Moq;

namespace Invoklet.NonUI.Tests.App.Services.Fixtures;

internal sealed class UpdateCheckServiceFixture
{
    public SessionSettings Session { get; } = new();
    public Mock<IPreferencesService> Preferences { get; } = new();
    public Mock<IReleaseMetadataReader> Metadata { get; } = new();

    public UpdateCheckServiceFixture()
    {
        Preferences.Setup(p => p.Session).Returns(Session);
    }

    public void LatestTag(string? tag) => Metadata
        .Setup(m => m.ReadStringAsync(It.IsAny<Uri>(), "tag_name", It.IsAny<CancellationToken>()))
        .ReturnsAsync(tag);

    public UpdateCheckService CreateSut() => new(Preferences.Object, Metadata.Object);
}
