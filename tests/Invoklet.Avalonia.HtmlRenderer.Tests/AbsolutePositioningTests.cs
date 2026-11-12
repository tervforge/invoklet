using Invoklet.Avalonia.HtmlRenderer.External.Core;
using Invoklet.Avalonia.HtmlRenderer.External.Core.Dom;
using Invoklet.Avalonia.HtmlRenderer.Tests.Fakes;
using Xunit;

namespace Invoklet.Avalonia.HtmlRenderer.Tests;

public class AbsolutePositioningTests
{
    [Fact]
    public void ClassStyles_ApplyAbsolutePositionToAnchor()
    {
        var adapter = new TestAdapter();
        var container = new HtmlContainerInt(adapter);
        var css = CssData.Parse(adapter, "div.shell { position:relative; } a.copy { position:absolute; top:4px; right:7px; width:16px; height:16px; }", false);

        container.SetHtml("<div class=\"shell\"><a class=\"copy\" href=\"#\">⧉</a><pre>code</pre></div>", css);

        var shell = FindByClass(container.Root, "shell");
        var copy = FindByClass(container.Root, "copy");

        Assert.NotNull(shell);
        Assert.NotNull(copy);
        Assert.Equal("relative", shell!.Position);
        Assert.Equal("absolute", copy!.Position);
        Assert.Equal("7px", copy.Right);
        Assert.Equal("4px", copy.Top);
        Assert.Same(shell, copy.ParentBox);
    }

    [Fact]
    public void ClassStyles_ApplyBottomAndRightToAbsoluteElements()
    {
        var adapter = new TestAdapter();
        var container = new HtmlContainerInt(adapter);
        var css = CssData.Parse(adapter, "div.shell { position:relative; } a.copy { position:absolute; bottom:6px; right:9px; }", false);

        container.SetHtml("<div class=\"shell\"><a class=\"copy\" href=\"#\">copy</a></div>", css);

        var copy = FindByClass(container.Root, "copy");

        Assert.NotNull(copy);
        Assert.Equal("absolute", copy!.Position);
        Assert.Equal("9px", copy.Right);
        Assert.Equal("6px", copy.Bottom);
    }

    private static CssBox? FindByClass(CssBox box, string className)
    {
        if (box.HtmlTag?.TryGetAttribute("class") == className)
            return box;

        foreach (var child in box.Boxes)
        {
            var match = FindByClass(child, className);
            if (match is not null)
                return match;
        }

        return null;
    }
}
