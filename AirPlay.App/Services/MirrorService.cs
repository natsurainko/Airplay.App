using AirPlay.App.FFmmpeg;
using AirPlay.App.Windows;
using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinUIEx;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager) : IHostedService
{
    private readonly ConcurrentDictionary<DeviceSession, H264Decoder> _mirroringDecodes = [];
    private readonly ConcurrentDictionary<DeviceSession, MirrorWindow> _mirroringWindows = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                MirrorWindow? mirrorWindow = null;
                H264Decoder decoder = new();

                session.MirrorController!.H264DataReceived += (_, e) =>
                {
                    if (decoder.Decode(e.Data, out var rgbData, out var width, out var height))
                    {
                        var window = mirrorWindow;
                        if (window != null)
                            window.OnFrameDataReceived(rgbData, width, height);
                        else
                            ArrayPool<byte>.Shared.Return(rgbData);
                    }
                    else Debug.WriteLine("Decode Failed");
                };

                session.MirrorController!.FrameSizeChanged += (_, e) =>
                {
                    if (_mirroringWindows.TryGetValue(session, out mirrorWindow))
                    {
                        App.DispatcherQueue.TryEnqueue(() => mirrorWindow.OnFrameSizeChanged(e));
                        return;
                    }

                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        mirrorWindow = new(session, e);
                        mirrorWindow.Show();

                        _mirroringWindows.TryAdd(session, mirrorWindow);
                    });
                };

                _mirroringDecodes.TryAdd(session, decoder);
            };

            session.MirrorControllerClosed += (_, _) =>
            {
                if (_mirroringWindows.TryRemove(session, out var mirrorWindow))
                    App.DispatcherQueue.TryEnqueue(() => mirrorWindow.Close());

                if (_mirroringDecodes.TryRemove(session, out var decoder))
                    decoder.Dispose();
            };
        };

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
