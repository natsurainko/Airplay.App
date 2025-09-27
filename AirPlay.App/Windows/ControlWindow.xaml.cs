using WinUIEx;

namespace AirPlay.App.Windows;

public sealed partial class ControlWindow : WindowEx
{
    public ControlWindow()
    {
        InitializeComponent();

        this.Width = 400;
        this.Height = 560;

        this.ExtendsContentIntoTitleBar = true;
        this.IsTitleBarVisible = false;

        this.Move(16, 16);
    }

    private void WindowEx_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
            this.Hide();
    }
}