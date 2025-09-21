using AirPlay.Models;
using Microsoft.Extensions.Hosting;
using NAudio.Wave;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AirPlay.App.Services;

class AudioPlayService : IHostedService
{
    private readonly IAirPlayReceiver _airPlayReceiver;

    private readonly BufferedWaveProvider _bufferedWaveProvider;
    private readonly WaveOutEvent _waveOut;

    public AudioPlayService(IAirPlayReceiver airPlayReceiver)
    {
        _airPlayReceiver = airPlayReceiver ?? throw new ArgumentNullException(nameof(airPlayReceiver));

        var waveFormat = new WaveFormat(44100, 16, 2);

        _bufferedWaveProvider = new(waveFormat)
        {
            BufferLength = 10 * waveFormat.AverageBytesPerSecond,
            DiscardOnBufferOverflow = true
        };

        _waveOut = new WaveOutEvent();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _waveOut.Init(_bufferedWaveProvider);
        _waveOut.Play();

        _airPlayReceiver.OnPCMDataReceived += AirPlayReceiver_OnPCMDataReceived;
        return Task.CompletedTask;
    }

    private void AirPlayReceiver_OnPCMDataReceived(object? sender, PcmData e)
    {
        //if (e.Data.Any(b => b != 0))
            _bufferedWaveProvider.AddSamples(e.Data, 0, e.Data.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();

        return Task.CompletedTask;
    }
}
