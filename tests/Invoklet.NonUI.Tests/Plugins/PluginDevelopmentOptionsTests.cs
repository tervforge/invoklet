using Invoklet.Plugins.Hosting;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

public sealed class PluginDevelopmentOptionsTests
{
    [Fact]
    public void Parse_Should_PreferTheFlagAndPreserveScriptArguments()
    {
        var parsed = PluginDevelopmentOptions.Parse(["run", "--plugins-dev", "my plugins", "test.inv", "--", "--plugins-dev"], "other");

        Assert.Equal(Path.GetFullPath("my plugins"), parsed.Directory);
        Assert.Equal(["run", "test.inv", "--", "--plugins-dev"], parsed.RemainingArguments);
    }

    [Fact]
    public void Parse_Should_UseTheEnvironmentWhenTheFlagIsAbsent()
    {
        Assert.Equal(Path.GetFullPath("my plugins"), PluginDevelopmentOptions.Parse([], "my plugins").Directory);
        Assert.Null(PluginDevelopmentOptions.Parse([], " ").Directory);
    }

    [Fact]
    public void Parse_Should_RejectMissingAndDuplicateDirectories()
    {
        Assert.Throws<ArgumentException>(() => PluginDevelopmentOptions.Parse(["--plugins-dev", "--"], null));
        Assert.Throws<ArgumentException>(() => PluginDevelopmentOptions.Parse(["--plugins-dev", "one", "--plugins-dev", "two"], null));
    }
}
