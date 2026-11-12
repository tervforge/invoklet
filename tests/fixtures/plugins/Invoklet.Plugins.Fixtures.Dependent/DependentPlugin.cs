using Invoklet.Plugins;
using Invoklet.Plugins.Fixtures.Library;

namespace Invoklet.Plugins.Fixtures.Dependent;

/// <summary>The manifest beside it says <c>invoklet.fixture.dependent</c> 1.0.0, depending on <c>invoklet.fixture.library</c>.</summary>
public sealed class DependentPlugin : LibraryPluginBase
{
    public const string Id = "invoklet.fixture.dependent";

    public override PluginDescriptor Descriptor { get; } = new(Id, "1.0.0");
}
