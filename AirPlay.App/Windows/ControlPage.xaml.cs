using AirPlay.App.Extensions;
using AirPlay.App.Models;
using AirPlay.App.Services;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WinUIEx;

namespace AirPlay.App.Windows;

public sealed partial class ControlPage : Page
{
    private readonly SmtcControlService _smtcControlService = ((App)App.Current).Host.Services.GetService<SmtcControlService>()!;

    public ControlPageVM VM => (ControlPageVM)DataContext;

    public ControlPage()
    {
        this.DataContext = ((App)App.Current).Host.Services.GetService<ControlPageVM>()!;
        InitializeComponent();

        this.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        App.TaskbarIcon!.IconSource = App.GetIconTheme(this.ActualTheme == ElementTheme.Dark);
    }

    private void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (sender as Button)!;

        if (Enum.TryParse<MediaControlCommand>(button.Tag.ToString(), out var command))
            _smtcControlService.SendMediaControlCommand(command);
    }

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        VM.PropertyChanged += OnPropertyChanged;

        ControlWindow controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();
        controlWindow.SetFocus();

        controlWindow.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                controlWindow.Hide();
        };
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Device")
        {
            ControlPopup.IsOpen = VM.Device?.EnableControl ?? false;
            VolumePopup.IsOpen = VM.Device?.EnableControl ?? false;
        }
    }
}

public partial class ControlPageVM : ObservableObject
{
    private readonly SessionManager _sessionManager;
    private readonly SmtcControlService _smtcControlService;

    public ObservableCollection<Device> Devices { get; init; } = [];

    [ObservableProperty]
    public partial Device? Device { get; set; }

    public bool ShowNoDevice => Devices.Count == 0;

    public ControlPageVM(SessionManager sessionManager, SmtcControlService smtcControlService)
    {
        _sessionManager = sessionManager;
        _smtcControlService = smtcControlService;

        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionClosed += OnSessionClosed;
    }

    partial void OnDeviceChanged(Device? value)
    {
        _smtcControlService.SwitchDevice(value);
    }

    private void OnSessionCreated(object? sender, DeviceSession e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            ControlWindow controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();
            controlWindow.Activate();
            controlWindow.SetForegroundWindow();
            controlWindow.SetFocus();

            Device device = new(e);
            Devices.Add(device);
            OnPropertyChanged(nameof(ShowNoDevice));

            Device ??= device;
        });
    }

    private void OnSessionClosed(object? sender, DeviceSession e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            if (Devices.FirstOrDefault(d => d.Session == e) is Device device)
                Devices.Remove(device);

            OnPropertyChanged(nameof(ShowNoDevice));
        });
    }
}