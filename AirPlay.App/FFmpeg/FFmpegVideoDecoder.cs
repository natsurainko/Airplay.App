using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using AirPlay.App.Extensions;
using AirPlay.Core2.Models.Messages.Mirror;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;

namespace AirPlay.App.FFmpeg;

/// <summary>Shared software decoding and aligned BGRA conversion for H.264 and HEVC.</summary>
public abstract unsafe class FFmpegVideoDecoder : IVideoDecoder
{
    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private int _failureCount;
    private bool _loggedFirstFrame;

    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;

    public MirrorVideoCodec Codec { get; }

    public bool Disposed { get; private set; }

    protected FFmpegVideoDecoder(MirrorVideoCodec codecType, AVCodecID codecId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        Codec = codecType;
        _logger.VideoDecoderInitializing(Codec);
        try
        {
            AVCodec* codec = ffmpeg.avcodec_find_decoder(codecId);
            if (codec == null) throw new ApplicationException($"{codecType} codec not found.");

            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_codecContext == null) throw new ApplicationException("Could not allocate codec context.");
            if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
                throw new ApplicationException($"Could not open {codecType} codec.");

            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_frame == null || _packet == null)
                throw new ApplicationException("Could not allocate decoder frame or packet.");

            _logger.VideoDecoderInitialized(Codec, ffmpeg.avcodec_version());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Decode(byte[] encodedData, [NotNullWhen(true)] out byte[]? rgbData, out int width, out int height)
    {
        rgbData = null;
        width = height = 0;

        lock (_lock)
        {
            if (Disposed || encodedData.Length == 0) return false;

            // FFmpeg bitstream readers require AV_INPUT_BUFFER_PADDING_SIZE zero bytes.
            // An owned packet also remains valid if the decoder retains a reference to it.
            ffmpeg.av_packet_unref(_packet);
            int allocationResult = ffmpeg.av_new_packet(_packet, encodedData.Length);
            if (allocationResult < 0) return ReportFailure("av_new_packet", allocationResult);

            try
            {
                encodedData.AsSpan().CopyTo(new Span<byte>(_packet->data, encodedData.Length));
                int ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (ret < 0) return ReportFailure("avcodec_send_packet", ret);

                ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
                if (ret < 0)
                {
                    // AVERROR(EAGAIN): needing more input is not a decoding failure.
                    if (ret == -ffmpeg.EAGAIN) return false;
                    return ReportFailure("avcodec_receive_frame", ret);
                }

                width = _frame->width;
                height = _frame->height;
                if (width <= 0 || height <= 0) return ReportFailure("Invalid frame dimensions");

                if (!_loggedFirstFrame)
                {
                    _loggedFirstFrame = true;
                    _logger.FirstVideoFrameDecoded(Codec, width, height, (AVPixelFormat)_frame->format,
                        _frame->linesize[0], _frame->linesize[1], _frame->linesize[2]);
                }

                int rgbStride = checked(width * 4);
                int rgbLength = checked(rgbStride * height);
                AVFrame* rgbFrame = ffmpeg.av_frame_alloc();
                if (rgbFrame == null) return ReportFailure("av_frame_alloc");
                SwsContext* swsCtx = null;
                byte[]? outputBuffer = null;

                try
                {
                    rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
                    rgbFrame->width = width;
                    rgbFrame->height = height;
                    int rgbAllocationResult = ffmpeg.av_frame_get_buffer(rgbFrame, 32);
                    if (rgbAllocationResult < 0)
                        return ReportFailure("av_frame_get_buffer", rgbAllocationResult);

                    swsCtx = ffmpeg.sws_getContext(
                        width, height, (AVPixelFormat)_frame->format,
                        width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                        (int)SwsFlags.SWS_BILINEAR, null, null, null);
                    if (swsCtx == null) return ReportFailure("sws_getContext");

                    int convertedRows = ffmpeg.sws_scale(
                        swsCtx, _frame->data, _frame->linesize, 0, height,
                        rgbFrame->data, rgbFrame->linesize);
                    if (convertedRows != height || rgbFrame->linesize[0] < rgbStride)
                        return ReportFailure("sws_scale", convertedRows);

                    // Upload tightly packed BGRA, without FFmpeg's aligned row padding.
                    outputBuffer = ArrayPool<byte>.Shared.Rent(rgbLength);
                    for (int y = 0; y < height; y++)
                    {
                        new ReadOnlySpan<byte>(rgbFrame->data[0] + y * rgbFrame->linesize[0], rgbStride)
                            .CopyTo(outputBuffer.AsSpan(y * rgbStride, rgbStride));
                    }
                    rgbData = outputBuffer;
                }
                finally
                {
                    if (outputBuffer != null && rgbData == null)
                        ArrayPool<byte>.Shared.Return(outputBuffer);
                    if (swsCtx != null) ffmpeg.sws_freeContext(swsCtx);
                    ffmpeg.av_frame_free(&rgbFrame);
                }
                return true;
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    private bool ReportFailure(string operation, int? result = null)
    {
        int count = ++_failureCount;
        if (count <= 3 || count % 300 == 0)
            _logger.VideoDecodeFailed(Codec, operation, result, count);
        return false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (Disposed) return;
            Disposed = true;
            if (_frame != null)
            {
                fixed (AVFrame** frame = &_frame) ffmpeg.av_frame_free(frame);
            }
            if (_packet != null)
            {
                fixed (AVPacket** packet = &_packet) ffmpeg.av_packet_free(packet);
            }
            if (_codecContext != null)
            {
                fixed (AVCodecContext** context = &_codecContext) ffmpeg.avcodec_free_context(context);
            }
        }
    }
}
