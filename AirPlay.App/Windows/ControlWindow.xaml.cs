using AirPlay.App.Extensions;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WinRT.Interop;
using WinUIEx;
using WinUIEx.Messaging;

namespace AirPlay.App.Windows;

public sealed partial class ControlWindow : WindowEx
{
    public static XamlRoot ControlWindowXamlRoot { get; private set; } = null!;

    private WindowMessageMonitor? _messageMonitor;

    public ControlWindow()
    {
        this.Width = 400;
        this.Height = 560;

        this.ExtendsContentIntoTitleBar = true;
        this.IsTitleBarVisible = false;
        this.IsAlwaysOnTop = true;
        this.IsShownInSwitchers = false;

        this.Move(16, 16);

        InitializeComponent();
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == 0x0100)
        {
            if (e.Message.WParam == 0x1B)
                this.Hide();
        }
        else if (e.Message.MessageId == 0x0312)
        {
            this.Activate();
            this.SetForegroundWindow();
            this.SetFocus();
        }
    }

    private void Frame_Loaded(object sender, RoutedEventArgs e)
    {
        ControlWindowXamlRoot = Frame.XamlRoot;

        Frame.Navigate(typeof(ControlPage));
        _messageMonitor = new WindowMessageMonitor(this);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

        PInvoke.RegisterHotKey
        (
            new HWND(WindowNative.GetWindowHandle(this)),
            1,
            HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_ALT,
            0x41
        );
    }
}