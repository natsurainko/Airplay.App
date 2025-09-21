using AirPlay.Models;
using AirPlay.Models.Audio;
using AirPlay.Services;
using AirPlay.Services.Implementations;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AirPlay.App.Services;

class SmtcControlService(IAirPlayReceiver airPlayReceiver, DacpDiscoveryService dacpDiscoveryService) : IHostedService
{
    private readonly MediaPlayer _player = new();
    private SystemMediaTransportControlsTimelineProperties? _lastTimeline;
    private Session? _audioSession;

    public SystemMediaTransportControls Smtc => _player.SystemMediaTransportControls;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _player.CommandManager.IsEnabled = false;
        Smtc.ButtonPressed += Smtc_ButtonPressed;

        airPlayReceiver.OnTrackInfoValueReceived += OnTrackInfoValueReceived;
        airPlayReceiver.OnPCMDataReceived += OnPCMDataReceived;
        SessionManager.Current.OnSessionsAddedOrUpdated += OnSessionsAddedOrUpdated;
        dacpDiscoveryService.OnDacpServiceShutdown += OnDacpServiceShutdown;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _player.Dispose();
        return Task.CompletedTask;
    }

    private void OnSessionsAddedOrUpdated(object? sender, Session e)
    {
        if (_audioSession == null && e.DacpId != null)
        {
            _audioSession = e;
            OnDacpServiceFoundChanged(true);
            return;
        }

        if (_audioSession != null && (_audioSession.DacpId == null || _audioSession.DacpEndPoint == null))
        {
            _audioSession = null;
            OnDacpServiceFoundChanged(false);
        }
    }

    private void OnDacpServiceShutdown(object? sender, System.Net.IPEndPoint e)
    {
        if (_audioSession?.DacpEndPoint == null)
        {
            _audioSession = null;
            OnDacpServiceFoundChanged(false);
        }
    }

    private void OnDacpServiceFoundChanged(bool e)
    {
        Smtc.IsEnabled = e;
        Smtc.IsPlayEnabled = e;
        Smtc.IsPauseEnabled = e;
        Smtc.IsNextEnabled = e;
        Smtc.IsPreviousEnabled = e;

        Smtc.DisplayUpdater.AppMediaId = "AirPlay";
        Smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
        Smtc.DisplayUpdater.Update();
    }

    private void OnTrackInfoValueReceived(object? sender, TrackInfoValue e)
    {
        switch (e.Type)
        {
            case TrackInfoType.Name:
                Smtc.DisplayUpdater.MusicProperties.Title = e.Value.ToString();
                break;
            case TrackInfoType.Artist:
                Smtc.DisplayUpdater.MusicProperties.Artist = e.Value.ToString();
                break;
            case TrackInfoType.Album:
                Smtc.DisplayUpdater.MusicProperties.AlbumTitle = e.Value.ToString();
                break;
            case TrackInfoType.Cover:
                string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "cover.jpg");
                File.WriteAllBytes(path, e.Value as byte[] ?? Array.Empty<byte>());
                Smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(StorageFile.GetFileFromPathAsync(path).GetAwaiter().GetResult());
                break;
            case TrackInfoType.ProgressDuration:
                var timeline = new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    MaxSeekTime = TimeSpan.FromSeconds((long)e.Value),
                    Position = _lastTimeline?.Position ?? TimeSpan.Zero,
                    EndTime = TimeSpan.FromSeconds((long)e.Value)
                };
                Smtc.UpdateTimelineProperties(timeline);
                _lastTimeline = timeline;
                break;
            case TrackInfoType.ProgressPosition:
                var _timeline = new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    MaxSeekTime = _lastTimeline?.MaxSeekTime ?? TimeSpan.Zero,
                    Position = TimeSpan.FromSeconds((long)e.Value),
                    EndTime = _lastTimeline?.EndTime ?? TimeSpan.Zero
                };
                Smtc.UpdateTimelineProperties(_timeline);
                _lastTimeline = _timeline;
                break;
            default:
                break;
        }

        Smtc.DisplayUpdater.Update();
    }

    private void OnPCMDataReceived(object? sender, PcmData e)
    {
        if (e.Data.Any(b => b != 0))
            Smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        else Smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
    }

    private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        string? command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => "play",
            SystemMediaTransportControlsButton.Pause => "pause",
            SystemMediaTransportControlsButton.Stop => "stop",
            SystemMediaTransportControlsButton.FastForward => "beginff",
            SystemMediaTransportControlsButton.Rewind => "beginrew",
            SystemMediaTransportControlsButton.Next => "nextitem",
            SystemMediaTransportControlsButton.Previous => "previtem",
            _ => null
        };

        if (string.IsNullOrEmpty(command)) return;
        if (_audioSession == null) return;

        _ = dacpDiscoveryService.SendCommandAsync(_audioSession, command);
    }
}
