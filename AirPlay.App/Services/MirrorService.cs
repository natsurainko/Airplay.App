using AirPlay.App.Windows;
using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WinUIEx;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager) : IHostedService
{
    private readonly ConcurrentDictionary<DeviceSession, MirrorWindow> _mirroringSession = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    MirrorWindow mirrorWindow = new(session);
                    _mirroringSession.TryAdd(session, mirrorWindow);

                    mirrorWindow.Show();
                });
            };

            session.MirrorControllerClosed += (_, _) =>
            {
                if (_mirroringSession.TryRemove(session, out var mirrorWindow))
                {
                    App.DispatcherQueue.TryEnqueue(() => mirrorWindow.Close());
                }
            };
        };

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
