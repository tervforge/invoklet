using Invoklet.Plugins;

namespace Invoklet.Plugins.Fixtures.Sample;

/// <summary>Loads and initializes; the manifest beside it says <c>invoklet.fixture.sample</c> 1.0.0.</summary>
public sealed class SamplePlugin : IInvokletPlugin
{
    public const string Id = "invoklet.fixture.sample";

    public PluginDescriptor Descriptor { get; } = new(Id, "1.0.0");

    public PluginContext? Context { get; private set; }

    public void Initialize(PluginContext context) => Context = context;
}

/// <summary>A second well-behaved plugin, for tests that need two different ids.</summary>
public sealed class SecondPlugin : IInvokletPlugin
{
    public const string Id = "invoklet.fixture.second";

    public PluginDescriptor Descriptor { get; } = new(Id, "2.0.0");
}

/// <summary>Throws while initializing.</summary>
public sealed class ThrowingPlugin : IInvokletPlugin
{
    public const string Id = "invoklet.fixture.throwing";

    public PluginDescriptor Descriptor { get; } = new(Id, "1.0.0");

    public void Initialize(PluginContext context) => throw new InvalidOperationException("This plugin always fails to initialize.");
}

/// <summary>Describes itself as a plugin other than the one its manifest names.</summary>
public sealed class MismatchedPlugin : IInvokletPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.fixture.other", "9.9.9");
}

/// <summary>A type the manifest may name by mistake: not a plugin at all.</summary>
public sealed class NotAPlugin
{
}
