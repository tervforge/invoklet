using Invoklet.Plugins;

namespace Invoklet.Plugins.Fixtures.Library;

/// <summary>What a dependant plugin derives from: a type that must come from the library's own load context.</summary>
public abstract class LibraryPluginBase : IInvokletPlugin
{
    public abstract PluginDescriptor Descriptor { get; }
}

/// <summary>The library plugin itself; the manifest beside it says <c>invoklet.fixture.library</c> 1.0.0 with the library role.</summary>
public sealed class LibraryPlugin : LibraryPluginBase
{
    public const string Id = "invoklet.fixture.library";

    public override PluginDescriptor Descriptor { get; } = new(Id, "1.0.0");
}
