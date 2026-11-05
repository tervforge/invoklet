using Avalonia.Interactivity;
using Invoklet.Services;
using Invoklet.TemplatedControls;

namespace Invoklet.Windows;

// No default button: Enter must not hand the secrets over without a choice.
public partial class SecretAccessDialog : InvokletDialogWindow
{
    public SecretAccessDialog(string scriptName, string secretName, bool isSave)
    {
        InitializeComponent();
        Layout.Heading = isSave ? $"Let “{scriptName}” change your secrets?" : $"Let “{scriptName}” read your secrets?";
        Layout.Message = (isSave ? $"It wants to change “{secretName}”." : $"It asks for “{secretName}”.") + " Allow only scripts you trust.";
    }

    private void Deny_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Deny);
    private void Once_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Once);
    private void Always_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Always);
}
