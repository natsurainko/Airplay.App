using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Mirror;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Timers;
using WinUIEx;

using Timer = System.Timers.Timer;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private readonly H264Decoder _h264Decoder = new();
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private CanvasDevice _device = CanvasDevice.GetSharedDevice();

    private int _frameCount = 0;
    private CanvasBitmap? frameBitmap;

    public MirrorWindow(DeviceSession deviceSession)
    {
        Width = 0;
        Height = 0;

        deviceSession.MirrorController!.FrameSizeChanged += OnFrameSizeChanged;
        deviceSession.MirrorController!.H264DataReceived += OnH264DataReceived;

        InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        Debug.WriteLine($"Ö¡ÂÊ: {_frameCount} fps");
        Interlocked.Exchange(ref _frameCount, 0);
    }

    private void OnH264DataReceived(object? sender, H264Data e)
    {
        Interlocked.Increment(ref _frameCount);

        if (_h264Decoder.Decode(e.Data, out var rgbData, out var width, out var height))
        {
            App.DispatcherQueue.TryEnqueue(() =>
            {
                frameBitmap = CanvasBitmap.CreateFromBytes
                (
                    _device,
                    rgbData,
                    width,
                    height,
                    global::Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized
                );

                Canvas.Invalidate();
            });
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
        _timer.Stop();
        _timer.Dispose();
        _h264Decoder.Dispose();
    }

    private void Canvas_Draw(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasDrawEventArgs args)
    {
        if (frameBitmap != null)
            args.DrawingSession.DrawImage(frameBitmap, 0, 0);
    }
}
