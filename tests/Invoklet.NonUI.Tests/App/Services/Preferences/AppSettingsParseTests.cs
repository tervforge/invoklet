using System.Text.Json;
using Invoklet.Services.Preferences;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services.Preferences;

// A settings file that is valid JSON but holds null where a group of settings should be must not stop Invoklet from starting.
public sealed class AppSettingsParseTests
{
    [Theory]
    [InlineData("""{"Logging":null}""")]
    [InlineData("""{"Keyboard":null}""")]
    [InlineData("""{"Appearance":null,"Editor":null,"Debugger":null,"CSharp":null,"Layout":null,"Session":null,"Experimental":null,"System":null}""")]
    [InlineData("""{"Keyboard":{"Bindings":null}}""")]
    [InlineData("""{"Layout":{"CollapsedSidebarSections":null,"CollapsedConnectionGroups":null,"CollapsedSampleFolders":null}}""")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Parse_NullsAndMissingGroups_GiveSettingsAllOfWhichCanBeRead(string json)
    {
        // Act
        var settings = AppSettings.Parse(json);

        // Assert: what the rest of Invoklet reads without checking for null, and the migration it runs at start.
        Assert.NotNull(settings.Appearance);
        Assert.NotNull(settings.Editor);
        Assert.NotNull(settings.Debugger);
        Assert.NotNull(settings.CSharp);
        Assert.NotNull(settings.Layout);
        Assert.NotNull(settings.Session);
        Assert.NotNull(settings.Experimental);
        Assert.NotNull(settings.System);
        Assert.NotNull(settings.Logging);
        _ = settings.Logging.Level;
        Assert.NotNull(settings.Keyboard.Bindings);
        Assert.False(settings.Keyboard.MigrateLegacy());
        Assert.NotNull(settings.Layout.CollapsedSidebarSections);
        Assert.NotNull(settings.Layout.CollapsedConnectionGroups);
        Assert.NotNull(settings.Layout.CollapsedSampleFolders);
    }

    [Fact]
    public void Parse_AValueThatIsThere_IsKept()
    {
        var settings = AppSettings.Parse("""{"Layout":{"SidebarWidth":417,"CollapsedSidebarSections":["Scripts"]},"Logging":null}""");

        Assert.Equal(417, settings.Layout.SidebarWidth);
        Assert.Equal(["Scripts"], settings.Layout.CollapsedSidebarSections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    public void Parse_TextThatIsNotJson_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => AppSettings.Parse(json));
    }
}
