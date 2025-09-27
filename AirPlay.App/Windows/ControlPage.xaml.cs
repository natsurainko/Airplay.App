using AirPlay.App.Models;
using AirPlay.App.Services;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace AirPlay.App.Windows;


public sealed partial class ControlPage : Page
{
    private readonly SmtcControlService _smtcControlService = ((App)App.Current).Host.Services.GetService<SmtcControlService>()!;

    public ControlPageVM VM { get; } = ((App)App.Current).Host.Services.GetService<ControlPageVM>()!;

    public ControlPage()
    {
        InitializeComponent();
    }

    private void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (sender as Button)!;

        if (Enum.TryParse<MediaControlCommand>(button.Tag.ToString(), out var command))
            _smtcControlService.SendMediaControlCommand(command);
    }
}

public partial class ControlPageVM : ObservableObject
{
    private readonly SessionManager _sessionManager;
    private readonly SmtcControlService _smtcControlService;

    public ObservableCollection<Device> Devices { get; init; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowControlBorder))]
    public partial Device? Device { get; set; }

    public bool ShowControlBorder => Device != null;

    public bool ShowNoDevice => Devices.Count == 0;

    public ControlPageVM(SessionManager sessionManager, SmtcControlService smtcControlService)
    {
        _sessionManager = sessionManager;
        _smtcControlService = smtcControlService;

        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionClosed += OnSessionClosed;
    }

    partial void OnDeviceChanged(Device? value) => _smtcControlService.SwitchDevice(value);

    private void OnSessionCreated(object? sender, DeviceSession e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            Devices.Add(new(e));
            OnPropertyChanged(nameof(ShowNoDevice));
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