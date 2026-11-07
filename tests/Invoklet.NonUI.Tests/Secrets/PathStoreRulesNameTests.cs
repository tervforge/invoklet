using Invoklet.Secrets;
using Xunit;

namespace Invoklet.NonUI.Tests.Secrets;

// The names of the Path Store, checked once for the window, the command line and Util.GetPath.
[Trait("Platform", "Independent")]
public sealed class PathStoreRulesNameTests
{
    [Theory]
    [InlineData("data", "data")]
    [InlineData("  work-space_1.0 ", "work-space_1.0")]
    public void Name_AValidName_IsTrimmed(string name, string expected)
        => Assert.Equal(expected, PathStoreRules.Name(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("my data")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    public void Name_AnInvalidName_Throws(string? name)
        => Assert.Throws<ArgumentException>(() => PathStoreRules.Name(name));

    [Fact]
    public void Name_LongerThanAHundredCharacters_Throws()
        => Assert.Throws<ArgumentException>(() => PathStoreRules.Name(new string('a', 101)));
}
