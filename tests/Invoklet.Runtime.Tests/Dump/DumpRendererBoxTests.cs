using Invoklet.Runtime;
using Xunit;

namespace Invoklet.Runtime.Tests.Dump;

[Trait("Platform", "Independent")]
public sealed class DumpRendererBoxTests
{
    private sealed record Address(string City);
    private sealed record Person(string Name, Address Home);
    private sealed class Throwing { public string Bad => throw new InvalidOperationException("no value"); }

    private sealed class Cycle { public Cycle? Self { get; set; } }

    private static int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;

    [Fact]
    public void Render_ATable_IsAnOpenBoxWithItsTitleFirst()
    {
        // Arrange
        int[] value = [1, 2];

        // Act
        var html = DumpRenderer.Render(value, null);

        // Assert
        Assert.StartsWith("<div class=\"dp-box\" data-dp-node=\"", html);
        Assert.DoesNotContain("data-dp-collapsed", html);
        Assert.Contains("><div class=\"dp-count\">Int32[2]</div>", html);
    }

    public static TheoryData<object, string> Collections => new()
    {
        { new[] { "a", "b" }, "String[2]" },
        { new List<int> { 1, 2 }, "List&lt;Int32&gt; (2 items)" },
        { new List<int> { 1 }, "List&lt;Int32&gt; (1 item)" },
        { new List<string>(), "(0 items)" },
        { new int[0], "(0 items)" },
        { new[] { 1, 2, 3 }.Select(x => x * 2), "IEnumerable&lt;Int32&gt; (3 items)" },
        { new Dictionary<string, int> { ["a"] = 1 }, "Dictionary&lt;String, Int32&gt; (1 item)" },
        { new[] { new { A = 1 } }, "\u00F8[1]" },
        { new[] { new[] { 1 } }, "Int32[][1]" },
    };

    [Theory]
    [MemberData(nameof(Collections))]
    public void Render_ACollection_TitlesItAsLinqPadDoes(object value, string expected)
    {
        // Arrange (the value is the argument)

        // Act
        var html = DumpRenderer.Render(value, null);

        // Assert
        Assert.Contains($"<div class=\"dp-count\">{expected}</div>", html);
    }

    [Fact]
    public void Render_MoreRowsThanTheLimit_SaysItShowsTheFirstOnes()
    {
        // Arrange
        var value = Enumerable.Range(0, 5).ToList();

        // Act
        var html = DumpRenderer.Render(value, null, maxRows: 3);

        // Assert
        Assert.Contains("(First 3 items)", html);
    }

    [Fact]
    public void Render_AnObjectInARow_IsAFoldedBoxThatStillHoldsItsValues()
    {
        // Arrange
        Person[] value = [new("Ann", new Address("Oslo"))];

        // Act
        var html = DumpRenderer.Render(value, null);

        // Assert
        Assert.Equal(1, Count(html, "data-dp-collapsed=\"1\""));
        Assert.Contains("Oslo", html);
        Assert.Contains("Ann", html);
    }

    [Fact]
    public void Render_ForceExpansion_FoldsNothing()
    {
        // Arrange
        Person[] value = [new("Ann", new Address("Oslo"))];

        // Act
        var html = DumpRenderer.Render(value, null, new DumpOptions { ForceExpansion = true });

        // Assert
        Assert.DoesNotContain("data-dp-collapsed", html);
    }

    [Fact]
    public void Render_CollapseToZero_FoldsTheOutermostToo()
    {
        // Arrange
        int[] value = [1];

        // Act
        var html = DumpRenderer.Render(value, null, new DumpOptions { CollapseTo = 0 });

        // Assert
        Assert.StartsWith("<div class=\"dp-box\"", html);
        Assert.Contains("data-dp-collapsed=\"1\"", html);
    }

    [Fact]
    public void Render_TwoDumps_NameTheirBoxesApart()
    {
        // Arrange
        int[] value = [1];

        // Act
        var (first, second) = (DumpRenderer.Render(value, null), DumpRenderer.Render(value, null));

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Render_AGetterThatThrows_ShowsItsMessage()
    {
        // Arrange
        var value = new Throwing();

        // Act
        var html = DumpRenderer.Render(value, null);

        // Assert
        Assert.Contains("dp-error", html);
    }

    [Fact]
    public void Render_ACycle_EndsAtTheDepthLimit()
    {
        // Arrange
        var value = new Cycle();
        value.Self = value;

        // Act
        var html = DumpRenderer.Render(value, null, new DumpOptions { MaxDepth = 3 });

        // Assert
        Assert.Equal(3, Count(html, "class=\"dp-box\""));
        Assert.Contains("dp-truncated", html);
    }

    [Fact]
    public void Render_TheMarkup_StaysWellFormedForTheCommandLine()
    {
        // Arrange
        Person[] value = [new("Ann", new Address("Oslo"))];

        // Act
        var html = DumpRenderer.Render(value, "Team");
        var root = System.Xml.Linq.XElement.Parse("<root>" + html + "</root>");

        // Assert
        Assert.Contains(root.Descendants(), e => e.Attribute("data-dp-node") is not null);
    }
}
