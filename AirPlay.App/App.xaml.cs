using AirPlay.App.Services;
using AirPlay.Models.Configs;
using AirPlay.Services;
using AirPlay.Services.Implementations;
using H.NotifyIcon;
using Makaretu.Dns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace AirPlay.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public IHost Host { get; set; }

    private TaskbarIcon? TaskbarIcon { get; set; }

    private DispatcherQueue DispatcherQueue { get; init; } = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        //AllocConsole();

        _ = SessionManager.Current;

        var builder = new HostBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions();
                services.Configure<AirPlayReceiverConfig>(c =>
                {
                    c.AirPlayPort = 7000;
                    c.AirTunesPort = 5000;
                    c.Instance = "AirPlay App";
                    c.DeviceMacAddress = "11:22:33:44:55:66";
                });

                services.AddSingleton<MulticastService>();
                services.AddSingleton<IAirPlayReceiver, AirPlayReceiver>();
                services.AddHostedService<AirPlayService>();
                services.AddHostedService<SmtcControlService>();

                services.AddSingleton<AudioPlayService>();
                services.AddHostedService<AudioPlayService>(p => p.GetRequiredService<AudioPlayService>());

                services.AddSingleton<DacpDiscoveryService>();
                services.AddHostedService<DacpDiscoveryService>(p => p.GetRequiredService<DacpDiscoveryService>());
            });

        Host = builder.Start();

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        XamlUICommand leftButtonCommand = new();
        XamlUICommand command = new()
        {
            IconSource = new SymbolIconSource() { Symbol = Symbol.Cancel },
            Label = "Exit",
            Description = "Exit the application"
        };

        command.ExecuteRequested += (s, e) => Current.Exit();
        //leftButtonCommand.ExecuteRequested += LeftButtonCommand_ExecuteRequested;

        var item = new MenuFlyoutItem()
        {
            Text = "Quit",
            Command = command
        };

        TaskbarIcon = new()
        {
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ToolTipText = "AirPlay.App",
            NoLeftClickDelay = true,
            IconSource = GetIconTheme(ShouldSystemUseDarkMode()),
            LeftClickCommand = leftButtonCommand,
            ContextFlyout = new MenuFlyout()
            {
                Items = 
                { 
                    new MenuFlyoutItem()
                    {
                        Text = "Quit",
                        Command = command
                    } 
                }
            }
        };

        TaskbarIcon.ForceCreate(false);

        UISettings uISettings = new UISettings();
        uISettings.ColorValuesChanged += UISettings_ColorValuesChanged;
    }

    private void UISettings_ColorValuesChanged(UISettings sender, object args)
    {
        RegistryKey root = Registry.CurrentUser;
        RegistryKey rk = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")!;

        DispatcherQueue.TryEnqueue(() =>
        {
            TaskbarIcon!.IconSource = GetIconTheme(isLightTheme: Convert.ToInt32(rk.GetValue("AppsUseLightTheme", null)) == 0);
        });
    }

    private static BitmapImage GetIconTheme(bool isLightTheme = false)
    {
        return new BitmapImage(new Uri
            ($"ms-appx:///Assets/Icons/airplay_x16{(isLightTheme ? "_light" : string.Empty)}.ico",
            uriKind: UriKind.RelativeOrAbsolute));
    }

    [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
    public static extern bool ShouldSystemUseDarkMode();
}
