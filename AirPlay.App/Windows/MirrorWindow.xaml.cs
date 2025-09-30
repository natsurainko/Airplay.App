using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
    private readonly Size _frameSize;

    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();

    private int _frameCount = 0;
    private CanvasBitmap? frameBitmap;

    public MirrorWindow(Size size)
    {
        _frameSize = size;

        InitializeComponent();

        Width = size.Width / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;
        Height = size.Height / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;

        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);

        this.ExtendsContentIntoTitleBar = true;
        this.IsMaximizable = false;

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        Debug.WriteLine($"FPS: {_frameCount} ");
        Interlocked.Exchange(ref _frameCount, 0);
    }

    public void OnFrameDataReceived(byte[] frameData)
    {
        try
        {
            if (this.WindowState == WindowState.Minimized) return;
            if (frameBitmap != null)
            {
                App.DispatcherQueue.TryEnqueue(() => Canvas.Invalidate()); 
                return;
            }

            frameBitmap?.Dispose();
            frameBitmap = CanvasBitmap.CreateFromBytes
            (
                _device,
                frameData,
                _frameSize.Width,
                _frameSize.Height,
                global::Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized
            );

            App.DispatcherQueue.TryEnqueue(() => Canvas.Invalidate());
        }
        finally
        {
            Interlocked.Increment(ref _frameCount);
            frameData = null!;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (frameBitmap != null)
        {
            args.DrawingSession.DrawImage(frameBitmap);

            frameBitmap.Dispose();
            frameBitmap = null;
        }
    }

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Canvas.RemoveFromVisualTree();
        this.Canvas = null;
    }
}
