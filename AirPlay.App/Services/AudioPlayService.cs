using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AirPlay.App.Services;

class AudioPlayService : IHostedService
{
    private readonly WaveOutEvent _waveOut;
    private readonly MixingSampleProvider _mixingSampleProvider;

    private readonly ConcurrentDictionary<DeviceSession, (BufferedWaveProvider, ISampleProvider)> _sampleProviders = [];

    public AudioPlayService(SessionManager sessionManager)
    {
        WaveFormat waveFormat = new (44100, 16, 2);

        _waveOut = new WaveOutEvent();
        _mixingSampleProvider = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) 
        {
            ReadFully = true
        };

        sessionManager.SessionCreated += (_, session) =>
        {
            session.AudioControllerCreated += (_, _) =>
            {
                BufferedWaveProvider bufferedWaveProvider = new(waveFormat)
                {
                    BufferLength = 10 * waveFormat.AverageBytesPerSecond,
                    DiscardOnBufferOverflow = true
                };

                session.AudioController!.AudioDataReceived += (sender, e) =>
                {
                    bufferedWaveProvider?.AddSamples(e.Data, 0, e.Data.Length);
                };

                var sampleProvider = bufferedWaveProvider.ToSampleProvider();

                _sampleProviders.TryAdd(session, (bufferedWaveProvider, sampleProvider));
                _mixingSampleProvider.AddMixerInput(sampleProvider);
            };

            session.AudioControllerClosed += (_, _) =>
            {
                if (_sampleProviders.TryRemove(session, out var audioProvider))
                    _mixingSampleProvider.RemoveMixerInput(audioProvider.Item2);
            };
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _waveOut.Init(_mixingSampleProvider);
        _waveOut.Play();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();

        return Task.CompletedTask;
    }
}
