using Invoklet.Plugins;
using Xunit;

namespace Invoklet.NonUI.Tests.Plugins;

[Trait("Platform", "Independent")]
public class PluginManifestReaderTests
{
    private const string Full = """
        {
          "id": "invoklet.connector.postgres",
          "version": "0.13.0-beta.1",
          "displayName": "PostgreSQL",
          "publisher": "Invoklet",
          "roles": { "connection": 1, "library": "1" },
          "entry": { "assembly": "Invoklet.Connectors.Postgres.dll", "type": "Invoklet.Connectors.Postgres.Plugin" },
          "dependsOn": ["invoklet.library.efcore", "invoklet.library.efcore"],
          "minAppVersion": "0.13.0",
          "assets": { "themes": ["dark.json"] }
        }
        """;

    [Fact]
    public void Read_Should_ReadEveryField_OfAFullManifest()
    {
        var result = PluginManifestReader.Read(Full);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        var manifest = result.Manifest!;
        Assert.Equal("invoklet.connector.postgres", manifest.Id);
        Assert.Equal("0.13.0-beta.1", manifest.Version);
        Assert.Equal("PostgreSQL", manifest.DisplayName);
        Assert.Equal("Invoklet", manifest.Publisher);
        Assert.Equal(1, manifest.Roles[PluginRole.Connection]);
        Assert.Equal(1, manifest.Roles[PluginRole.Library]);
        Assert.Equal(new PluginEntryPoint("Invoklet.Connectors.Postgres.dll", "Invoklet.Connectors.Postgres.Plugin"), manifest.Entry);
        Assert.Equal(["invoklet.library.efcore"], manifest.DependsOn);
        Assert.Equal(new Version(0, 13, 0), manifest.MinAppVersion);
        Assert.True(manifest.RequiresCode);
    }

    [Fact]
    public void Read_Should_KeepUnknownFields_ForTheRolesThatReadThem()
    {
        var manifest = PluginManifestReader.Read(Full).Manifest!;

        var assets = Assert.Single(manifest.Extra);
        Assert.Equal("assets", assets.Key);
        Assert.Equal("dark.json", assets.Value.GetProperty("themes")[0].GetString());
    }

    [Fact]
    public void Manifest_Should_CopyAndProtectRoleAndDependencyCollections()
    {
        var roles = new Dictionary<PluginRole, int> { [PluginRole.Theme] = 1 };
        var dependencies = new List<string> { "invoklet.fixture.library" };
        var manifest = new PluginManifest("invoklet.theme.x", "1.0.0", "X", null, roles, null, dependencies, null,
            new Dictionary<string, System.Text.Json.JsonElement>());

        roles.Clear();
        dependencies.Clear();

        Assert.Equal(1, manifest.Roles[PluginRole.Theme]);
        Assert.Single(manifest.DependsOn);
        var exposedRoles = Assert.IsAssignableFrom<IDictionary<PluginRole, int>>(manifest.Roles);
        Assert.Throws<NotSupportedException>(() => exposedRoles.Add(PluginRole.Connection, 1));
    }

    [Fact]
    public void Read_Should_AcceptAThemeWithoutAnEntryPoint()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.midnight", "version": "1.0.0", "displayName": "Midnight", "roles": { "theme": 1 } }
            """);

        Assert.True(result.IsValid);
        Assert.Null(result.Manifest!.Entry);
        Assert.False(result.Manifest.RequiresCode);
        Assert.Null(result.Manifest.Publisher);
        Assert.Null(result.Manifest.MinAppVersion);
        Assert.Empty(result.Manifest.DependsOn);
    }

    [Fact]
    public void Read_Should_RequireAnEntryPoint_ForARoleWithCode()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.connector.x", "version": "1.0.0", "displayName": "X", "roles": { "connection": 1 } }
            """);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.ManifestInvalid, diagnostic.Code);
        Assert.Equal("invoklet.connector.x", diagnostic.PluginId);
        Assert.Contains("'entry'", diagnostic.Message);
    }

    [Theory]
    [InlineData("Invoklet.Connector.X")]
    [InlineData("invoklet")]
    [InlineData("invoklet..x")]
    [InlineData("invoklet.x-")]
    [InlineData("")]
    public void Read_Should_RejectAMalformedId_AndNameThePluginByItsFolder(string id)
    {
        var result = PluginManifestReader.Read($$"""
            { "id": "{{id}}", "version": "1.0.0", "displayName": "X", "roles": { "theme": 1 } }
            """, fallbackId: "folder-name");

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.ManifestInvalid, diagnostic.Code);
        Assert.Equal("folder-name", diagnostic.PluginId);
        Assert.Contains("'id'", diagnostic.Message);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0 beta")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-.")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3+build..x")]
    public void Read_Should_RejectAMalformedVersion(string version)
    {
        var result = PluginManifestReader.Read($$"""
            { "id": "invoklet.theme.x", "version": "{{version}}", "displayName": "X", "roles": { "theme": 1 } }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Code == PluginDiagnosticCode.ManifestInvalid && d.Message.Contains("'version'"));
    }

    [Fact]
    public void Read_Should_RejectARoleThisInvokletDoesNotKnow()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.x.y", "version": "1.0.0", "displayName": "X", "roles": { "theme": 1, "shell": 1 } }
            """);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PluginDiagnosticCode.UnsupportedRole, diagnostic.Code);
        Assert.Contains("shell", diagnostic.Detail);
    }

    [Fact]
    public void Read_Should_RejectAnEntryPointInADataOnlyPlugin()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": { "theme": 1 },
              "entry": { "assembly": "Plugin.dll", "type": "Example.Plugin" } }
            """);

        Assert.False(result.IsValid);
        Assert.Contains("data-only", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void Read_Should_RejectDuplicateRoleKeys()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": { "theme": 999, "theme": 1 } }
            """);

        Assert.False(result.IsValid);
        Assert.Equal(PluginDiagnosticCode.ManifestUnreadable, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Read_Should_KeepAnUnrecognizedRolePathOutOfTheUserMessage()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": { "C:/Users/Synthetic/private": 1 } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.DoesNotContain("C:/Users/Synthetic/private", diagnostic.Message);
        Assert.Contains("C:/Users/Synthetic/private", diagnostic.Detail);
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("{ \"theme\": 0 }")]
    [InlineData("{ \"theme\": \"one\" }")]
    [InlineData("[ \"theme\" ]")]
    public void Read_Should_RequireAtLeastOneRole_WithAPositiveContractVersion(string roles)
    {
        var result = PluginManifestReader.Read($$"""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": {{roles}} }
            """);

        Assert.False(result.IsValid);
        Assert.All(result.Diagnostics, d => Assert.Equal(PluginDiagnosticCode.ManifestInvalid, d.Code));
    }

    [Theory]
    [InlineData("{ \"assembly\": \"..\\\\other\\\\x.dll\", \"type\": \"X\" }")]
    [InlineData("{ \"assembly\": \"sub/x.dll\", \"type\": \"X\" }")]
    [InlineData("{ \"assembly\": \"x.exe\", \"type\": \"X\" }")]
    [InlineData("{ \"assembly\": \"x.dll\", \"type\": \"\" }")]
    [InlineData("{ \"assembly\": \"x.dll\" }")]
    [InlineData("\"x.dll\"")]
    public void Read_Should_RequireAnEntryAssemblyFileName_AndAType(string entry)
    {
        var result = PluginManifestReader.Read($$"""
            { "id": "invoklet.connector.x", "version": "1.0.0", "displayName": "X", "roles": { "connection": 1 }, "entry": {{entry}} }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Code == PluginDiagnosticCode.ManifestInvalid && d.Message.Contains("'entry"));
    }

    [Fact]
    public void Read_Should_RejectADependencyOnItself()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": { "theme": 1 }, "dependsOn": ["invoklet.theme.x"] }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("depend on itself"));
    }

    [Fact]
    public void Read_Should_RejectAMalformedMinimumVersion()
    {
        var result = PluginManifestReader.Read("""
            { "id": "invoklet.theme.x", "version": "1.0.0", "displayName": "X", "roles": { "theme": 1 }, "minAppVersion": "soon" }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("'minAppVersion'"));
    }

    [Theory]
    [InlineData("{ \"id\": ")]
    [InlineData("")]
    [InlineData("[]")]
    public void Read_Should_ReportTextThatIsNotAManifest(string json)
    {
        var result = PluginManifestReader.Read(json, fallbackId: "broken");

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(diagnostic.Code is PluginDiagnosticCode.ManifestUnreadable or PluginDiagnosticCode.ManifestInvalid);
        Assert.Equal("broken", diagnostic.PluginId);
    }

    [Fact]
    public void Read_Should_ReportEveryProblem_NotJustTheFirst()
    {
        var result = PluginManifestReader.Read("""
            { "id": "Bad", "version": "1", "roles": { "connection": 1 } }
            """, fallbackId: "folder");

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Diagnostics.Count); // id, version, displayName, entry
        Assert.All(result.Diagnostics, d => Assert.Equal("folder", d.PluginId));
    }
}
