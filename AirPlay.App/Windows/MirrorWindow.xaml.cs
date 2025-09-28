using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Mirror;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using WinUIEx;
using Timer = System.Timers.Timer;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private LibVLC? _libvlc;
    private Media? _media;
    private MediaPlayer? _mediaPlayer;

    private readonly Pipe _pipe = new();
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));

    private int _frameCount = 0;

    public MirrorWindow(DeviceSession deviceSession)
    {
        Width = 0;
        Height = 0;

        deviceSession.MirrorController!.FrameSizeChanged += OnFrameSizeChanged;
        deviceSession.MirrorController!.H264DataReceived += OnH264DataReceived;

        InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;

        VideoView.Initialized += VideoView_Initialized;
        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        Debug.WriteLine($"Ö¡ÂÊ: {_frameCount} fps");
        Interlocked.Exchange(ref _frameCount, 0);
    }

    private async void OnH264DataReceived(object? sender, H264Data e)
    {
        Interlocked.Increment(ref _frameCount);

        try
        {
            await _pipe.Writer.WriteAsync(e.Data);
            //_pipe.Writer.FlushAsync();
        }
        catch (Exception ex) 
        {  
        }
    }

    private void OnFrameSizeChanged(object? sender, Size e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            this.Width = e.Width;
            this.Height = e.Height;
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Task.Run(() =>
        {
            _mediaPlayer?.Stop();
        });

        _pipe.Writer.Complete();
        _pipe.Reader.Complete();

        _mediaPlayer?.Dispose();
        _media?.Dispose();
        _libvlc?.Dispose();

        _timer.Stop();
        _timer.Dispose();
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        Core.Initialize();

        _libvlc = new LibVLC(enableDebugLogs: true, [..e.SwapChainOptions, "--h264-fps=60"]);
        _mediaPlayer = new MediaPlayer(_libvlc);

        var pipeStream = new PipeReaderStream(_pipe.Reader);

        _media = new Media(_libvlc, new StreamMediaInput(pipeStream), 
            ":demux=h264",
            ":network-caching=20",
            ":live-caching=20",
            ":clock-jitter=0",
            ":clock-synchro=0"
        );

        VideoView.MediaPlayer = _mediaPlayer;
        _mediaPlayer!.Play(_media!);
    }
}
