using AirPlay.App.Extensions;
using AirPlay.App.Windows;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Processors.Mirror;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AirPlay.App.FFmpeg;
using WinUIEx;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager, ILoggerFactory loggerFactory) : IHostedService
{
    private readonly ILogger<MirrorService> _logger = loggerFactory.CreateLogger<MirrorService>();
    private readonly ConcurrentDictionary<DeviceSession, IVideoDecoder> _mirroringDecoders = [];
    private readonly ConcurrentDictionary<DeviceSession, MirrorWindow> _mirroringWindows = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                var controller = session.MirrorController!;
                var sync = new object();
                bool closed = false;
                MirrorWindow? mirrorWindow = null;
                Action? unsubscribeFrames = null;

                void OnProcessorRequested(object? sender, MirrorProcessorRequestedEventArgs request)
                {
                    // Selection is synchronous on the mirror worker, not dispatched to the UI.
                    lock (sync)
                    {
                        if (closed) return;

                        unsubscribeFrames?.Invoke();
                        unsubscribeFrames = null;
                        if (_mirroringDecoders.TryRemove(session, out var previousDecoder))
                            previousDecoder.Dispose();

                        _logger.MirrorCodecDetected(session.DeviceDisplayName, request.Codec);

                        MirrorVideoProcessor? processor = request.Codec switch
                        {
                            MirrorVideoCodec.H264 => new H264MirrorVideoProcessor(),
                            MirrorVideoCodec.Hevc => new HevcMirrorVideoProcessor(),
                            _ => null
                        };
                        if (processor is not { IsSupported: true })
                        {
                            // Leaving Processor null explicitly declines unsupported codecs.
                            _logger.MirrorCodecUnsupported(session.DeviceDisplayName, request.Codec);
                            return;
                        }

                        IVideoDecoder decoder;
                        try
                        {
                            decoder = CreateVideoDecoder(request.Codec);
                        }
                        catch (Exception ex)
                        {
                            _logger.MirrorDecoderInitializationFailed(ex, session.DeviceDisplayName, request.Codec);
                            throw;
                        }

                        // Decoder callbacks run synchronously under the session lock below.
                        void OnDecodedFrame(byte[] rgbData, int width, int height)
                        {
                            if (mirrorWindow != null)
                                mirrorWindow.OnFrameDataReceived(rgbData, width, height);
                            else
                                ArrayPool<byte>.Shared.Return(rgbData);
                        }

                        Action<byte[], int, int> onDecodedFrame = OnDecodedFrame;
                        bool loggedFirstPayload = false;
                        void OnFrameReceived(byte[] data)
                        {
                            lock (sync)
                            {
                                if (closed || !ReferenceEquals(controller.VideoProcessor, processor)) return;

                                if (!loggedFirstPayload)
                                {
                                    loggedFirstPayload = true;
                                    _logger.FirstMirrorPayload(session.DeviceDisplayName, decoder.Codec, data.Length);
                                }

                                try
                                {
                                    decoder.Decode(data, onDecodedFrame);
                                }
                                catch (Exception ex)
                                {
                                    _logger.MirrorFrameProcessingFailed(ex, session.DeviceDisplayName);
                                    throw;
                                }
                            }
                        }

                        switch (processor)
                        {
                            case H264MirrorVideoProcessor h264:
                            {
                                void OnH264FrameReceived(object? frameSender, H264Data frame) => OnFrameReceived(frame.Data);
                                h264.FrameReceived += OnH264FrameReceived;
                                unsubscribeFrames = () => h264.FrameReceived -= OnH264FrameReceived;
                                break;
                            }
                            case HevcMirrorVideoProcessor hevc:
                            {
                                void OnHevcFrameReceived(object? frameSender, HevcData frame) => OnFrameReceived(frame.Data);
                                hevc.FrameReceived += OnHevcFrameReceived;
                                unsubscribeFrames = () => hevc.FrameReceived -= OnHevcFrameReceived;
                                break;
                            }
                        }
                        _mirroringDecoders[session] = decoder;
                        request.Processor = processor;
                    }
                }

                void OnFrameSizeChanged(object? sender, System.Drawing.Size size)
                {
                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        lock (sync)
                        {
                            if (closed) return;

                            if (mirrorWindow != null)
                            {
                                mirrorWindow.OnFrameSizeChanged(size);
                                return;
                            }

                            mirrorWindow = new MirrorWindow(session, size, loggerFactory.CreateLogger<MirrorWindow>());
                            mirrorWindow.Show();
                            _logger.MirrorWindowCreated(session.DeviceDisplayName, size.Width, size.Height);
                            _mirroringWindows.TryAdd(session, mirrorWindow);
                        }
                    });
                }

                void OnControllerClosed(object? sender, EventArgs args)
                {
                    lock (sync)
                    {
                        closed = true;
                        controller.ProcessorRequested -= OnProcessorRequested;
                        controller.FrameSizeChanged -= OnFrameSizeChanged;
                        session.MirrorControllerClosed -= OnControllerClosed;
                        unsubscribeFrames?.Invoke();
                        unsubscribeFrames = null;

                        if (_mirroringWindows.TryRemove(session, out var window))
                            App.DispatcherQueue.TryEnqueue(() => window.Close());
                        if (_mirroringDecoders.TryRemove(session, out var decoder))
                            decoder.Dispose();
                    }
                }

                controller.ProcessorRequested += OnProcessorRequested;
                controller.FrameSizeChanged += OnFrameSizeChanged;
                session.MirrorControllerClosed += OnControllerClosed;
            };
        };

        return Task.CompletedTask;
    }

    private IVideoDecoder CreateVideoDecoder(MirrorVideoCodec codec) => codec switch
    {
        MirrorVideoCodec.H264 => new H264Decoder(loggerFactory.CreateLogger<H264Decoder>()),
        MirrorVideoCodec.Hevc => new HevcDecoder(loggerFactory.CreateLogger<HevcDecoder>()),
        _ => throw new NotSupportedException($"No video decoder is available for {codec}.")
    };

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
