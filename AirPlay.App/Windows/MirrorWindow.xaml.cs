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
using System.Timers;
using WinUIEx;

using Timer = System.Timers.Timer;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();

    private int _frameCountPerMin = 0;
    private CanvasBitmap? _frameBitmap;
    private Size _frameSize;

    public MirrorWindow(DeviceSession session, Size size)
    {
        Session = session;
        _frameSize = size;

        //this.IsMinimizable = false;
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
            if (_frameBitmap != null)
            {
                Canvas.Invalidate();
                return;
            }

            _frameBitmap = CanvasBitmap.CreateFromBytes
            (
                _device,
                frameData,
                _frameSize.Width,
                _frameSize.Height,
                global::Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized
            );

            Canvas.Invalidate();
        }
        catch (Exception)
        {

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
        _frameBitmap?.Dispose();

        _timer.Stop();
        _timer.Dispose();
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_frameBitmap == null) return;

        try
        {
            lock (_frameBitmap)
                args.DrawingSession.DrawImage(_frameBitmap);
        }
        catch (ObjectDisposedException) { }
        finally
        {
            _frameBitmap.Dispose();
            _frameBitmap = null;
        }
    }

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Canvas.RemoveFromVisualTree();
        this.Canvas = null;
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

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        Popup.IsOpen = true;

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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.Minimize();

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await ConfirmDialog.ShowAsync();

    private void ConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => Session.Disconnect();
}