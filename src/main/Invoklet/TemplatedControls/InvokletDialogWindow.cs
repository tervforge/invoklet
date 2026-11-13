using Avalonia.Controls;

namespace Invoklet.TemplatedControls;

/// <summary>Common native window behavior for compact application dialogs.</summary>
public class InvokletDialogWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public InvokletDialogWindow()
    {
        Width = 460;
        MinWidth = 320;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
