using AirPlay.App.Configurations;
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
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

namespace AirPlay.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly AppConfiguration _configuration = AppConfiguration.Load();

    public IHost Host { get; set; }

    public static TaskbarIcon? TaskbarIcon { get; set; }

    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.UnhandledException += (_, e) => PInvoke.MessageBox(
            default, e.Exception?.ToString() ?? e.Message, "AirPlay — 未处理的异常",
            MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);

        DynamicallyLoadedBindings.LibrariesPath = Path.Combine(Package.Current.InstalledPath, "Libraries");
        DynamicallyLoadedBindings.Initialize();

        var builder = new HostBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.UseAirPlayService();

                services.AddSingleton(_configuration);
                services.Configure<AirPlayConfig>(c => c.ServiceName = "AirPlay Windows App");
                services.Configure<AirTunesConfig>(c =>
                {
                    c.MirrorWidth = _configuration.MirrorWidth;
                    c.MirrorHeight = _configuration.MirrorHeight;
                    c.MirrorMaxFps = _configuration.MirrorMaxFps;
                });

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
                    new MenuFlyoutItem
                    {
                        Text = "Changes apply after restarting the app",
                        IsEnabled = false
                    },
                    new MenuFlyoutSeparator(),
                    CreateChoiceMenu(
                        "Mirror resolution",
                        [
                            ("1080p (1920 × 1080)", 1080),
                            ("1440p (2560 × 1440)", 1440),
                            ("2160p (3840 × 2160)", 2160)
                        ],
                        _configuration.MirrorHeight,
                        AppConfiguration.SaveMirrorHeight
                    ),
                    CreateChoiceMenu(
                        "Maximum frame rate",
                        [
                            ("30 FPS", 30),
                            ("60 FPS", 60),
                            ("120 FPS", 120)
                        ],
                        _configuration.MirrorMaxFps,
                        AppConfiguration.SaveMirrorMaxFps
                    ),
                    new MenuFlyoutSeparator(),
                    new MenuFlyoutItem()
                    {
                        Text = "Quit",
                        Command = command
                    } 
                }
            }
        };
        TaskbarIcon.ForceCreate(false);

        Host.Services.GetRequiredService<ControlWindow>().Activate();
    }

    private static MenuFlyoutSubItem CreateChoiceMenu(string title, (string Label, int Value)[] choices, int selectedValue, Action<int> save)
    {
        var menu = new MenuFlyoutSubItem { Text = title };
        var items = new List<ToggleMenuFlyoutItem>();

        foreach (var choice in choices)
        {
            var command = new XamlUICommand { Label = choice.Label };
            var item = new ToggleMenuFlyoutItem
            {
                Text = choice.Label,
                IsChecked = choice.Value == selectedValue,
                Command = command
            };
            // Native PopupMenu invokes commands rather than XAML Click events.
            command.ExecuteRequested += (_, _) =>
            {
                save(choice.Value);
                foreach (var other in items)
                    other.IsChecked = ReferenceEquals(other, item);
            };
            items.Add(item);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void LeftButtonCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) => App.DispatcherQueue.TryEnqueue(() =>
    {
        ControlWindow controlWindow = Host.Services.GetRequiredService<ControlWindow>();
        controlWindow.Activate();
        controlWindow.SetForegroundWindow();
        controlWindow.SetFocus();
    });

    public static BitmapImage GetIconTheme(bool isLightTheme = false) 
        => new(new Uri($"ms-appx:///Assets/Icons/airplay_x16{(isLightTheme ? "_light" : string.Empty)}.ico",
            uriKind: UriKind.RelativeOrAbsolute));

    [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
    public static extern bool ShouldSystemUseDarkMode();
}
