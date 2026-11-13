using Avalonia.Controls;
using Invoklet.Plugins;
using Invoklet.TemplatedControls;

namespace Invoklet.Windows;

internal sealed class PluginPackageConfirmationDialog : InvokletDialogWindow
{
    public PluginPackageConfirmationDialog(string title, string message, string action, DialogKind kind = DialogKind.Question)
    {
        Title = title;
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = kind == DialogKind.Warning };
        var confirm = new Button { Content = action, IsDefault = kind != DialogKind.Warning };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);
        Content = new DialogLayout
        {
            Heading = title,
            Message = message,
            Kind = kind,
            Actions = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, confirm },
            },
        };
    }

    public static PluginPackageConfirmationDialog ForInstall(PluginManifest manifest, string path)
    {
        var roles = string.Join(", ", manifest.Roles.Keys.Select(PluginRoles.Name).Order(StringComparer.Ordinal));
        var publisher = manifest.Publisher ?? "Not specified";
        var source = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var message = $"{manifest.DisplayName}\nID: {manifest.Id}\nVersion: {manifest.Version}\nRole: {roles}\nPublisher: {publisher}\nSource: {source}\n\n";
        if (manifest.RequiresCode)
            message += "This plugin runs with the same permissions as Invoklet. Approve it only if you trust its source.\n\n";
        message += "Approval applies to these package contents. The plugin will be available after restarting Invoklet.";
        return new PluginPackageConfirmationDialog("Install plugin?", message, "Approve and install",
            manifest.RequiresCode ? DialogKind.Warning : DialogKind.Question);
    }
}
