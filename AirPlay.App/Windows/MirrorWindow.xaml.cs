using AirPlay.Core2.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Windows.Graphics.DirectX;
using WinUIEx;

using Timer = System.Timers.Timer;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();
    private readonly Lock _bitmapLock = new();

    private int _frameCountPerMin = 0;
    private CanvasBitmap? _currentBitmap;
    private CanvasBitmap? _nextBitmap;
    private Size _frameSize;

    public MirrorWindow(DeviceSession session, Size size)
    {
        Session = session;
        _frameSize = size;

        this.IsMaximizable = false;

        this.IsTitleBarVisible = false;
        this.ExtendsContentIntoTitleBar = true;

        InitializeComponent();

        Width = size.Width / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;
        Height = size.Height / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;

        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    public void OnFrameSizeChanged(Size size)
    {
        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);
        _frameSize = size;
    }

    public void OnFrameDataReceived(byte[] frameData)
    {
        try
        {
            if (this.WindowState == WindowState.Minimized) return;

            lock (_bitmapLock)
            {
                if (_nextBitmap == null ||
                    _nextBitmap.Size.Width != _frameSize.Width ||
                    _nextBitmap.Size.Height != _frameSize.Height)
                {
                    _nextBitmap?.Dispose();
                    _nextBitmap = CanvasBitmap.CreateFromBytes(
                        _device,
                        frameData,
                        _frameSize.Width,
                        _frameSize.Height,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized
                    );
                }
                else _nextBitmap.SetPixelBytes(frameData);
            }

            Canvas.Invalidate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnFrameDataReceived error: {ex.Message}");
        }
        finally
        {
            Interlocked.Increment(ref _frameCountPerMin);
            ArrayPool<byte>.Shared.Return(frameData);
        }
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        Debug.WriteLine($"FPS: {_frameCountPerMin} ");
        Interlocked.Exchange(ref _frameCountPerMin, 0);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _currentBitmap?.Dispose();
        _nextBitmap?.Dispose();

        _timer.Stop();
        _timer.Dispose();

        GC.Collect();
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        lock (_bitmapLock)
        {
            if (_nextBitmap != null)
            {
                (_currentBitmap, _nextBitmap) = (_nextBitmap, _currentBitmap);
                args.DrawingSession.DrawImage(_currentBitmap);
            }
            else if (_currentBitmap != null)
            {
                args.DrawingSession.DrawImage(_currentBitmap);
            }
        }
    }

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

    public DeviceSession Session { get; private set; }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.Minimize();

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await ConfirmDialog.ShowAsync();

    private void ConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => Session.Disconnect();

    private async void Border_Loaded(object sender, RoutedEventArgs e)
    {
        this.AppWindow.TitleBar.SetDragRectangles(
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = (int)(Border.ActualWidth * Border.XamlRoot.RasterizationScale),
                Height = (int)(48 * Border.XamlRoot.RasterizationScale)
            }
        ]);

        await Task.Delay(500);
        Popup.IsOpen = true;
    }

    private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        this.AppWindow.TitleBar.SetDragRectangles(
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = (int)(Border.ActualWidth * Border.XamlRoot.RasterizationScale),
                Height = (int)(48 * Border.XamlRoot.RasterizationScale)
            }
        ]);
    }

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Canvas.RemoveFromVisualTree();
        this.Canvas = null;
    }
}