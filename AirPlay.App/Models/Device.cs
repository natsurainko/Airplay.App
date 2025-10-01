using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Models.Messages.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using Windows.Media;

namespace AirPlay.App.Models;

public partial class Device : ObservableObject
{
    private static readonly HttpClient _httpClient = new();
    private readonly Action<double> _setVolumeAction;

    public DeviceSession Session { get; private set; }

    public string DeviceIcon
    {
        get
        {
            if (string.IsNullOrEmpty(Session.DeviceModel)) return "\ue7f4";
            if (Session.DeviceModel.Contains("Phone")) return "\ue8ea";
            if (Session.DeviceModel.Contains("Pad")) return "\ue70a";

            return "\ue7f4";
        }
    }

    [ObservableProperty]
    public partial bool ShowVolumeIcon { get; set; }

    [ObservableProperty]
    public partial bool ShowMirrorIcon { get; set; }

    [ObservableProperty]
    public partial BitmapImage? Cover { get; set; }

    [ObservableProperty]
    public partial string? PlayingItemName { get; set; }

    [ObservableProperty]
    public partial string? Artist { get; set; }

    [ObservableProperty]
    public partial string? Album { get; set; }

    [ObservableProperty]
    public partial MediaProgressInfo? ProgressInfo { get; set; }

    [ObservableProperty]
    public partial bool EnableControl { get; set; }

    [ObservableProperty]
    public partial string PlayPauseIcon { get; set; } = "\uf5b0";

    [ObservableProperty]
    public partial string PlayPauseTag { get; set; } = "Play";

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial MediaPlaybackStatus PlaybackStatus { get; set; }

    public Device(DeviceSession deviceSession)
    {
        Session = deviceSession;
        EnableControl = deviceSession.DacpServiceEndPoint != null;
        Volume = deviceSession.Volume;

        deviceSession.AudioControllerCreated += OnAudioControllerCreated;

        deviceSession.MirrorControllerCreated += OnMirrorControllerCreated;
        deviceSession.MirrorControllerClosed += OnMirrorControllerClosed;

        deviceSession.MediaProgressInfoReceived += OnMediaProgressInfoReceived;
        deviceSession.MediaWorkInfoReceived += OnMediaWorkInfoReceived;
        deviceSession.MediaCoverReceived += OnMediaCoverReceived;
        deviceSession.RemoteSetVolumeRequest += OnRemoteSetVolumeRequest;

        deviceSession.DacpServiceFound += OnDacpServiceFound;

        _setVolumeAction = (arg) => _ = Session.SetVolumeAsync(arg, _httpClient);
        _setVolumeAction = _setVolumeAction.Debounce(500);
    }

    partial void OnVolumeChanged(double value)
    {
        if (Session.Volume == value) return;
        _setVolumeAction(value);
    }

    private void OnMirrorControllerCreated(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() => ShowMirrorIcon = true);

    private void OnMirrorControllerClosed(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() => ShowMirrorIcon = false);

    private void OnDacpServiceFound(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() => EnableControl = true);

    private void OnAudioControllerCreated(object? sender, EventArgs e)
    {
        Session.AudioController?.AudioDataReceived += OnAudioDataReceived;
    }

    private void OnRemoteSetVolumeRequest(object? sender, double e) => App.DispatcherQueue.TryEnqueue(() => Volume = e);

    private void OnAudioDataReceived(object? sender, PcmAudioData e)
    {
        bool value = e.Data.Any(b => b != 0);

        PlaybackStatus = e.Data.Any(b => b != 0)
            ? MediaPlaybackStatus.Playing
            : MediaPlaybackStatus.Paused;

        if (ShowVolumeIcon != value)
        {
            App.DispatcherQueue.TryEnqueue(() =>
            {
                ShowVolumeIcon = value;
                PlayPauseTag = value ? "Pause" : "Play";
                PlayPauseIcon = value ? "\uf8ae" : "\uf5b0";
            });
        }
    }

    private void OnMediaProgressInfoReceived(object? sender, MediaProgressInfo e) => App.DispatcherQueue.TryEnqueue(() => ProgressInfo = e);

    private void OnMediaWorkInfoReceived(object? sender, MediaWorkInfo e) => App.DispatcherQueue.TryEnqueue(() =>
    {
        PlayingItemName = e.Name;
        Artist = e.Artist;
        Album = e.Album;
    });

    private void OnMediaCoverReceived(object? sender, byte[] e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            using MemoryStream memoryStream = new(e);

            BitmapImage bitmapImage = new();
            bitmapImage.SetSource(memoryStream.AsRandomAccessStream());
            Cover = bitmapImage;
        });
    }
}
