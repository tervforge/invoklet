using Avalonia.Interactivity;
using Invoklet.Services.Setup;
using Invoklet.TemplatedControls;

namespace Invoklet.Windows;

/// <summary>What the setup is about to install, which version and where, before it downloads anything.</summary>
public partial class SetupInstallConfirmationDialog : InvokletDialogWindow
{
    public SetupInstallConfirmationDialog(SetupInstallPlan plan)
    {
        InitializeComponent();
        Layout.Heading = plan.Heading;
        Layout.Message = plan.Message;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Install_Click(object? sender, RoutedEventArgs e) => Close(true);
}
