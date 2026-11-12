using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Invoklet.Secrets;
using Invoklet.Windows;
using Invoklet.Windows.Models;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Windows;

public sealed class PathStoreWindowTests
{
    [AvaloniaFact]
    public void AddAndSave_Should_UseTheEditedPathAndDisableSaveForRelativeInput()
    {
        var store = new Mock<IPathStore>();
        store.Setup(s => s.LoadSnapshot()).Returns(new PathStoreSnapshot([], null));
        store.Setup(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), null)).Returns("saved");
        var folder = Path.GetFullPath("path-store-test-data");
        var window = new PathStoreWindow(new PathStoreWindowViewModel(store.Object, _ => false));
        window.Show();
        try
        {
            Click(window, "AddButton");
            var nameBox = window.FindControl<TextBox>("NameBox")!;
            var pathBox = window.FindControl<TextBox>("PathBox")!;
            var save = window.FindControl<Button>("OkButton")!;
            nameBox.SetCurrentValue(TextBox.TextProperty, "workspace");
            pathBox.SetCurrentValue(TextBox.TextProperty, "relative");
            Assert.False(save.IsEnabled);

            pathBox.SetCurrentValue(TextBox.TextProperty, folder);
            Assert.True(save.IsEnabled);
            Click(window, "OkButton");

            store.Verify(s => s.Save(It.Is<IReadOnlyList<NamedPath>>(paths =>
                paths.Count == 1 && paths[0] == new NamedPath("workspace", folder)), null), Times.Once);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [AvaloniaFact]
    public void LegacyWildcard_Should_BeEditableBeforeSaving()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Wildcards are legal filename characters on Unix.");
        var corrected = Path.GetFullPath("input.csv");
        var legacy = Path.Combine(Path.GetDirectoryName(corrected)!, "*.csv");
        var store = new Mock<IPathStore>();
        store.Setup(s => s.LoadSnapshot()).Returns(new PathStoreSnapshot([new("legacy", legacy)], "loaded"));
        store.Setup(s => s.Save(It.IsAny<IReadOnlyList<NamedPath>>(), "loaded")).Returns("saved");
        var window = new PathStoreWindow(new PathStoreWindowViewModel(store.Object, _ => false));
        window.Show();
        try
        {
            var pathBox = window.FindControl<TextBox>("PathBox")!;
            var save = window.FindControl<Button>("OkButton")!;
            Assert.Equal(legacy, pathBox.Text);
            Assert.False(save.IsEnabled);

            pathBox.SetCurrentValue(TextBox.TextProperty, corrected);
            Assert.True(save.IsEnabled);
            Click(window, "OkButton");

            store.Verify(s => s.Save(It.Is<IReadOnlyList<NamedPath>>(paths =>
                paths.Count == 1 && paths[0] == new NamedPath("legacy", corrected)), "loaded"), Times.Once);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    private static void Click(Window window, string name)
    {
        window.UpdateLayout();
        var button = window.FindControl<Button>(name)!;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
