using AirPlay.App.Extensions;
using AirPlay.App.Services;
using AirPlay.App.Windows;
using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Configs;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace AirPlay.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public IHost Host { get; set; }

    private TaskbarIcon? TaskbarIcon { get; set; }

    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.UnhandledException += (_, e) => System.Windows.Forms.MessageBox.Show(e.Message + e.Exception.ToString());

        DynamicallyLoadedBindings.LibrariesPath = Path.Combine(Package.Current.InstalledPath, "Libraries");
        DynamicallyLoadedBindings.Initialize();

        var builder = new HostBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.UseAirPlayService();

                services.Configure<AirPlayConfig>(c => c.ServiceName = "AirPlay App");

                services.AddSingleton<SmtcControlService>();
                services.AddHostedService(p => p.GetRequiredService<SmtcControlService>());

                services.AddSingleton<ControlPageVM>();
                services.AddHostedService<AudioPlayService>();
                services.AddHostedService<MirrorService>();

                services.AddSingleton<ControlWindow>();

                services.AddSerilog(configure =>
                {
                    configure.WriteTo.Logger(l => l.WriteTo
                        .Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}][{Level:u3}] <{SourceContext}>: {Message:lj}{NewLine}{Exception}"));
                });
            });

        Host = builder.Start();
        Host.Services.GetService<ControlPageVM>();
        Host.Services.GetService<ControlWindow>();

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();

        XamlUICommand leftButtonCommand = new();
        XamlUICommand command = new()
        {
            IconSource = new SymbolIconSource() { Symbol = Symbol.Cancel },
            Label = "Exit",
            Description = "Exit the application"
        };

        command.ExecuteRequested += (s, e) => Current.Exit();
        leftButtonCommand.ExecuteRequested += LeftButtonCommand_ExecuteRequested;

        var item = new MenuFlyoutItem()
        {
            Text = "Quit",
            Command = command
        };

        TaskbarIcon = new()
        {
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ToolTipText = "AirPlay App",
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

        UISettings uISettings = new();
        uISettings.ColorValuesChanged += UISettings_ColorValuesChanged;

        Host.Services.GetRequiredService<ControlWindow>().Activate();
    }

    private void LeftButtonCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) => App.DispatcherQueue.TryEnqueue(() =>
    {
        ControlWindow controlWindow = Host.Services.GetRequiredService<ControlWindow>();
        controlWindow.Activate();
        controlWindow.SetForegroundWindow();
        controlWindow.SetFocus();
    });

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
