using AirPlay.App.Extensions;
using AirPlay.Core2.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
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
    private readonly Lock _bitmapLock = new();

    private Size _frameSize;
    private int _decodedFrames = 0;
    private int _droppedFrames = 0;

    private CanvasDevice? _canvasDevice;
    private CanvasBitmap? _currentBitmap;

    private bool _isRendering = false;
    private bool _isDisposed = false;

    public MirrorWindow(DeviceSession session, Size size)
    {
        Session = session;
        _frameSize = size;

        this.IsMaximizable = false;
        this.IsTitleBarVisible = false;
        this.ExtendsContentIntoTitleBar = true;
        this.Title = session.DeviceDisplayName;

        InitializeComponent();

        Width = size.Width / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;
        Height = size.Height / ControlWindow.ControlWindowXamlRoot.RasterizationScale / 1.5;

        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);
        Canvas.TargetElapsedTime = TimeSpan.FromSeconds(1 / (double)this.GetRefreshRate());

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
        Interlocked.Increment(ref _decodedFrames);

        if (_isDisposed || _canvasDevice == null)
        {
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }
        if (this.WindowState == WindowState.Minimized)
        {
            Canvas.Paused = true;
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }
        if (_isRendering)
        {
            Interlocked.Increment(ref _droppedFrames);
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }

        _isRendering = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_isDisposed || _canvasDevice == null) return;

                lock (_bitmapLock)
                {
                    if (_currentBitmap == null ||
                        _currentBitmap.Size.Width != _frameSize.Width ||
                        _currentBitmap.Size.Height != _frameSize.Height)
                    {
                        _currentBitmap?.Dispose();
                        _currentBitmap = CanvasBitmap.CreateFromBytes(
                            _canvasDevice,
                            frameData,
                            _frameSize.Width,
                            _frameSize.Height,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized
                        );
                    }
                    else _currentBitmap.SetPixelBytes(frameData);
                }

                if (Canvas.Paused) Canvas.Paused = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnFrameDataReceived error: {ex.Message}");
            }
            finally
            {
                _isRendering = false;
                ArrayPool<byte>.Shared.Return(frameData);
            }
        });
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        var dropped = Interlocked.Exchange(ref _droppedFrames, 0);
        var fps = Interlocked.Exchange(ref _decodedFrames, 0);

        if (dropped > 0)
            Debug.WriteLine($"FPS: {fps} (Dropped: {dropped})");
        else
            Debug.WriteLine($"FPS: {fps}");
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        lock (_bitmapLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }

        _timer.Stop();
        _timer.Dispose();

        Canvas?.Paused = true;
        _canvasDevice = null;

        _isDisposed = true;
        GC.Collect();
    }

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_isDisposed) return;

        lock (_bitmapLock)
        {
            if (_currentBitmap != null)
                args.DrawingSession.DrawImage(_currentBitmap);
        }
    }

    private void Canvas_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        _canvasDevice = sender.Device;
        Debug.WriteLine($"Canvas device created: {_canvasDevice != null}");
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