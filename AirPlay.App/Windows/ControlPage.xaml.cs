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
using System.ComponentModel;
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
        ControlWindow controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();
        controlWindow.SetFocus();

        controlWindow.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                controlWindow.Hide();
        };
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

    public bool CanControl => Device?.EnableControl ?? false;

    public ControlPageVM(SessionManager sessionManager, SmtcControlService smtcControlService)
    {
        _sessionManager = sessionManager;
        _smtcControlService = smtcControlService;

        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionClosed += OnSessionClosed;
    }

    partial void OnDeviceChanged(Device? oldValue, Device? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnSelectedDevicePropertyChanged;
        if (newValue != null)
            newValue.PropertyChanged += OnSelectedDevicePropertyChanged;

        OnPropertyChanged(nameof(CanControl));
        _smtcControlService.SwitchDevice(newValue);
    }

    private void OnSelectedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AirPlay.App.Models.Device.EnableControl))
            OnPropertyChanged(nameof(CanControl));
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
            {
                bool wasSelected = ReferenceEquals(Device, device);
                Devices.Remove(device);

                if (wasSelected)
                    Device = Devices.FirstOrDefault();
            }

            OnPropertyChanged(nameof(ShowNoDevice));
        });
    }
}