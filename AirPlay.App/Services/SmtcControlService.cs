using AirPlay.App.Models;
using AirPlay.Core2.Models.Messages;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;

namespace AirPlay.App.Services;

public class SmtcControlService : IHostedService
{
    private Device? _device;
    private readonly MediaPlayer _player = new();
    private readonly HttpClient _httpClient = new();

    public SystemMediaTransportControls Smtc => _player.SystemMediaTransportControls;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _player.CommandManager.IsEnabled = false;
        Smtc.ButtonPressed += Smtc_ButtonPressed;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _player.Dispose();
        return Task.CompletedTask;
    }

    public void SwitchDevice(Device? device)
    {
        bool enable = device?.EnableControl ?? false;

        Smtc.IsEnabled = enable;
        Smtc.IsEnabled = enable;
        Smtc.IsPlayEnabled = enable;
        Smtc.IsPauseEnabled = enable;
        Smtc.IsNextEnabled = enable;
        Smtc.IsPreviousEnabled = enable;

        _device?.PropertyChanged -= OnPropertyChanged;
        _device?.Session.MediaWorkInfoReceived -= OnMediaWorkInfoReceived;

        _device = device;

        if (device != null)
        {
            Smtc.DisplayUpdater.AppMediaId = $"AirPlay ({device.PlayingItemName})";
            Smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            Smtc.DisplayUpdater.Update();
        }

        Smtc.DisplayUpdater.MusicProperties.Title = _device?.PlayingItemName ?? string.Empty;
        Smtc.DisplayUpdater.MusicProperties.Artist = _device?.Artist ?? string.Empty;
        Smtc.DisplayUpdater.MusicProperties.AlbumTitle = _device?.Album ?? string.Empty;
        Smtc.PlaybackStatus = _device?.PlaybackStatus ?? MediaPlaybackStatus.Stopped;

        //deviceSession.MediaProgressInfoReceived += OnMediaProgressInfoReceived;
        _device?.Session.MediaWorkInfoReceived += OnMediaWorkInfoReceived;
        //deviceSession.MediaCoverReceived += OnMediaCoverReceived;
        _device?.PropertyChanged += OnPropertyChanged;

        Smtc.DisplayUpdater.Update();
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Smtc.DisplayUpdater.MusicProperties.Title = _device?.PlayingItemName ?? string.Empty;
        Smtc.DisplayUpdater.MusicProperties.Artist = _device?.Artist ?? string.Empty;
        Smtc.DisplayUpdater.MusicProperties.AlbumTitle = _device?.Album ?? string.Empty;

        Smtc.PlaybackStatus = _device?.PlaybackStatus ?? MediaPlaybackStatus.Stopped;
    }

    private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        MediaControlCommand? command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaControlCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaControlCommand.Pause,
            SystemMediaTransportControlsButton.Stop => MediaControlCommand.Stop,
            //SystemMediaTransportControlsButton.FastForward => MediaControlCommand.,
            //SystemMediaTransportControlsButton.Rewind => MediaControlCommand.,
            SystemMediaTransportControlsButton.Next => MediaControlCommand.NextItem,
            SystemMediaTransportControlsButton.Previous => MediaControlCommand.PrevItem,
            _ => null
        };

        SendMediaControlCommand(command);
    }

    public void SendMediaControlCommand(MediaControlCommand? command)
    {
        if (command == null) return;

        _device?.Session.SendMediaControlCommandAsync(_httpClient, command.Value);
    }

    //private void OnMediaProgressInfoReceived(object? sender, MediaProgressInfo e) => App.DispatcherQueue.TryEnqueue(() => ProgressInfo = e);

    private void OnMediaWorkInfoReceived(object? sender, MediaWorkInfo e) => App.DispatcherQueue.TryEnqueue(() =>
    {
        Smtc.DisplayUpdater.MusicProperties.Title = e.Name;
        Smtc.DisplayUpdater.MusicProperties.Artist = e.Artist;
        Smtc.DisplayUpdater.MusicProperties.AlbumTitle = e.Album;

        Smtc.DisplayUpdater.Update();
    });

    //private void OnMediaCoverReceived(object? sender, byte[] e) { }
}
