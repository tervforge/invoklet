using Avalonia.Interactivity;
using Invoklet.TemplatedControls;

namespace Invoklet.Windows;

// A script allowed to use the secrets replacing or removing one already kept. No default button: Enter must not wipe a secret.
public partial class SecretChangeDialog : InvokletDialogWindow
{
    public SecretChangeDialog(string scriptName, string secretName, bool remove)
    {
        InitializeComponent();
        Layout.Heading = remove ? $"Let “{scriptName}” remove “{secretName}”?" : $"Let “{scriptName}” replace “{secretName}”?";
        Layout.Message = remove
            ? "The secret is kept in your Secret Store. Other scripts that use it won't find it."
            : "The secret is already in your Secret Store. Its value will be replaced.";
        AllowButton.Content = remove ? "Remove" : "Replace";
    }

    private void Deny_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Allow_Click(object? sender, RoutedEventArgs e) => Close(true);
}
