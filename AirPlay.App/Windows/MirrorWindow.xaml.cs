using AirPlay.App.FFmmpeg;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Mirror;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly DeviceSession _deviceSession;
    private readonly H264Decoder _h264Decoder = new();
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();

    private int _frameCount = 0;
    private CanvasBitmap? frameBitmap;

    public MirrorWindow(DeviceSession deviceSession)
    {
        _deviceSession = deviceSession;

        if (deviceSession.MirrorController?.FrameSize != null)
            (Width, Height) = (deviceSession.MirrorController.FrameSize.Value.Width, deviceSession.MirrorController.FrameSize.Value.Height);
        else (Width, Height) = (100, 300);

        deviceSession.MirrorController!.FrameSizeChanged += OnFrameSizeChanged;
        deviceSession.MirrorController!.H264DataReceived += OnH264DataReceived;

        InitializeComponent();

        Canvas.Width = Width;
        Canvas.Height = Height;

        this.ExtendsContentIntoTitleBar = true;

        //this.IsResizable = false;
        this.IsMaximizable = false;
        //this.IsMinimizable = false;

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
#if DEBUG
        Debug.WriteLine($"Ö¡ÂÊ: {_frameCount} fps");
#endif
        Interlocked.Exchange(ref _frameCount, 0);
    }

    private void OnH264DataReceived(object? sender, H264Data e)
    {
        Interlocked.Increment(ref _frameCount);

        if (!_h264Decoder.Disposed && _h264Decoder.Decode(e.Data, out var rgbData, out var width, out var height))
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
            (Width, Height) = (e.Width, e.Height);
            Canvas.Width = e.Width;
            Canvas.Height = e.Height;
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _deviceSession.MirrorController?.H264DataReceived -= OnH264DataReceived;

        _timer.Stop();
        _timer.Dispose();
        _h264Decoder.Dispose();
    }

    private void Canvas_Draw(Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasDrawEventArgs args)
    {
        if (frameBitmap != null)
            args.DrawingSession.DrawImage(frameBitmap, 0, 0);
    }

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Canvas.RemoveFromVisualTree();
        this.Canvas = null;
    }
}
