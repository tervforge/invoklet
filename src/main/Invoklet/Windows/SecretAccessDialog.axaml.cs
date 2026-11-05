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
        Layout.Message = (isSave
                ? $"The script wants to change the secret “{secretName}” in the Secret Store."
                : $"The script asks for the secret “{secretName}” from the Secret Store.")
            + " Allow it only for a script you trust: a script can send what it reads anywhere."
            + " Allow Once lasts until Invoklet is closed; Always Allow remembers this file.";
    }

    private void Deny_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Deny);
    private void Once_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Once);
    private void Always_Click(object? sender, RoutedEventArgs e) => Close(SecretAccessChoice.Always);
}
