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

                var offsetSampleProvider = new OffsetSampleProvider(bufferedWaveProvider.ToSampleProvider());
                offsetSampleProvider.DelayBy = session.VolumeDelay;
                var volumeSampleProvider = new VolumeSampleProvider(offsetSampleProvider);

                session.AudioController!.AudioDataReceived += (sender, e) =>
                {
                    volumeSampleProvider.Volume = (float)(session.Volume / 100);
                    bufferedWaveProvider?.AddSamples(e.Data, 0, e.Data.Length);
                };

                _sampleProviders.TryAdd(session, (bufferedWaveProvider, volumeSampleProvider));
                _mixingSampleProvider.AddMixerInput(volumeSampleProvider);
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
