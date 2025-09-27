using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Mirror;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Nito.ProducerConsumerStream;
using System.Drawing;
using System.Threading.Tasks;
using WinUIEx;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private LibVLC? _libvlc;
    private Media? _media;
    private MediaPlayer? _mediaPlayer;

    private readonly ProducerConsumerStream _producerConsumerStream = new();

    public MirrorWindow(DeviceSession deviceSession)
    {
        InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;

        deviceSession.MirrorController!.FrameSizeChanged += OnFrameSizeChanged;
        deviceSession.MirrorController!.H264DataReceived += OnH264DataReceived;

        VideoView.Initialized += VideoView_Initialized;
        Closed += OnWindowClosed;

        Width = 0;
        Height = 0;
    }

    private void OnH264DataReceived(object? sender, H264Data e)
    {
        Task.Run(async () =>
        {
            await _producerConsumerStream.Writer.WriteAsync(e.Data, 0, e.Length);
            await _producerConsumerStream.Writer.FlushAsync();
        });
    }

    private void OnFrameSizeChanged(object? sender, Size e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            this.Width = e.Width / 2.5;
            this.Height = e.Height / 2.5;
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Task.Run(() =>
        {
            _mediaPlayer?.Stopped += (s, e) => grid.Children.Remove(VideoView);
            _mediaPlayer?.Stop();
        });

        //_memoryStream?.Close();
        _producerConsumerStream.Writer.Close();
        _producerConsumerStream.Reader.Close();

        _mediaPlayer?.Dispose();
        _media?.Dispose();
        _libvlc?.Dispose();
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        Core.Initialize();

        _libvlc = new LibVLC(enableDebugLogs: true, e.SwapChainOptions);
        _mediaPlayer = new MediaPlayer(_libvlc);

        //_media = new Media(_libvlc, new StreamMediaInput(_memoryStream), ":demux=h264");
        _media = new Media(_libvlc, new StreamMediaInput(_producerConsumerStream.Reader), ":demux=h264", ":dshow-fps=60");

        _mediaPlayer.Play(_media);

        VideoView.MediaPlayer = _mediaPlayer;
    }
}
