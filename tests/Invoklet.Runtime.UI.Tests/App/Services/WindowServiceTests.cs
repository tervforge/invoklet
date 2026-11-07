using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Invoklet.Services;
using Invoklet.Windows;
using Moq;
using Xunit;

namespace Invoklet.Runtime.UI.Tests.App.Services;

// WindowService used to take the whole IServiceProvider and resolve
// DependenciesWindow/PreferencesWindow/IThemeService inside its methods (Service Locator), which
// meant testing it in isolation needed a full ServiceCollection/ServiceProvider just to exercise one
// method. Now it takes explicit Func<TWindow> factories plus IThemeService directly — these tests are
// the payoff: no DI container anywhere, just plain fakes.
public class WindowServiceTests
{
    [AvaloniaFact]
    public async Task ShowDependenciesAsync_Should_CreateItsWindow_ViaTheInjectedFactory()
    {
        var window = new DependenciesWindow();
        var factoryCalled = false;
        var service = new WindowService(
            dependenciesWindowFactory: () => { factoryCalled = true; return window; },
            preferencesWindowFactory: () => throw new InvalidOperationException("not expected here"),
            themeService: Mock.Of<IThemeService>());

        await service.ShowDependenciesAsync();

        Assert.True(factoryCalled);
    }

    [AvaloniaFact]
    public async Task ShowPreferencesAsync_Should_CreateItsWindow_ViaTheInjectedFactory()
    {
        var window = new PreferencesWindow();
        var factoryCalled = false;
        var service = new WindowService(
            dependenciesWindowFactory: () => throw new InvalidOperationException("not expected here"),
            preferencesWindowFactory: () => { factoryCalled = true; return window; },
            themeService: Mock.Of<IThemeService>());

        await service.ShowPreferencesAsync();

        Assert.True(factoryCalled);
    }

    // A dialog of a tab in a window of its own opens over that window; with no window in use, over the main one.
    [AvaloniaFact]
    public void OwnerOf_TheWindowInUse_IsTheOwnerOfADialog()
    {
        var main = new Window();
        var tab = new Window();
        main.Show();
        tab.Show();
        tab.Activate();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var owner = WindowService.OwnerOf([main, tab], main);

        Assert.True(tab.IsActive);
        Assert.Same(tab, owner);
        tab.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void OwnerOf_NoWindowInUse_IsTheMainWindow()
    {
        var main = new Window();
        var hidden = new Window();

        var owner = WindowService.OwnerOf([main, hidden], main);

        Assert.Same(main, owner);
    }
}
