using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using AirPlay.App.Extensions;
using AirPlay.Core2.Models.Messages.Mirror;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;

namespace AirPlay.App.FFmpeg;

/// <summary>Shared software decoding and tightly packed BGRA conversion for H.264 and HEVC.</summary>
public abstract unsafe class FFmpegVideoDecoder : IVideoDecoder
{
    // Extra space after the last row for SIMD accesses; this does not add per-row padding.
    private const int RgbTailPadding = 64;

    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private int _failureCount;
    private bool _loggedFirstFrame;
    private bool _draining;
    private bool _endOfStream;

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

    public void Decode(byte[] encodedData, Action<byte[], int, int> onFrame)
    {
        ArgumentNullException.ThrowIfNull(encodedData);
        ArgumentNullException.ThrowIfNull(onFrame);
        lock (_lock)
        {
            if (Disposed) return;
            if (_draining || _endOfStream)
                throw new InvalidOperationException("Cannot submit video after end of input; create a new decoder.");
            // Empty input is not an end-of-stream signal. Use Flush explicitly.
            if (encodedData.Length == 0) return;

            // Owned storage supplies FFmpeg's required zero padding and reference counting.
            ffmpeg.av_packet_unref(_packet);
            int allocationResult = ffmpeg.av_new_packet(_packet, encodedData.Length);
            if (allocationResult < 0)
            {
                ReportFailure("av_new_packet", allocationResult);
                return;
            }

            try
            {
                encodedData.AsSpan().CopyTo(new Span<byte>(_packet->data, encodedData.Length));
                if (SendPacket(_packet, onFrame))
                    ReceiveFrames(onFrame, out _);
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    public void Flush(Action<byte[], int, int> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        lock (_lock)
        {
            if (Disposed || _endOfStream) return;
            if (!_draining)
            {
                if (!SendPacket(null, onFrame)) return;
                _draining = true;
            }

            int result = ReceiveFrames(onFrame, out _);
            // Once the null packet is accepted, receive must reach EOF, not need more input.
            if (result == -ffmpeg.EAGAIN)
                ReportFailure("avcodec_receive_frame: EAGAIN after end of input", result);
        }
    }

    private bool SendPacket(AVPacket* packet, Action<byte[], int, int> onFrame)
    {
        while (true)
        {
            int result = ffmpeg.avcodec_send_packet(_codecContext, packet);
            if (result == 0) return true;
            if (result == ffmpeg.AVERROR_EOF)
            {
                _endOfStream = true;
                if (packet != null) ReportFailure("avcodec_send_packet", result);
                return false;
            }
            if (result != -ffmpeg.EAGAIN)
            {
                ReportFailure("avcodec_send_packet", result);
                return false;
            }

            // The input was NOT consumed. Receive pending output, then retry this exact packet.
            int receiveResult = ReceiveFrames(onFrame, out int received);
            if (receiveResult != -ffmpeg.EAGAIN) return false;
            if (received == 0)
            {
                // FFmpeg forbids both sides returning EAGAIN without progress. Do not busy-loop.
                ReportFailure("avcodec_send_packet/receive_frame: no progress", result);
                return false;
            }
        }
    }

    private int ReceiveFrames(Action<byte[], int, int> onFrame, out int received)
    {
        received = 0;
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (result < 0)
            {
                if (result == ffmpeg.AVERROR_EOF)
                    _endOfStream = true;
                else if (result != -ffmpeg.EAGAIN)
                    ReportFailure("avcodec_receive_frame", result);
                return result;
            }

            received++;
            try
            {
                // A conversion failure must not leave later decoded frames waiting in the codec.
                if (TryConvertFrame(out var rgbData, out int width, out int height))
                    onFrame(rgbData, width, height);
            }
            finally
            {
                // Also release the native frame if conversion or the consumer throws.
                ffmpeg.av_frame_unref(_frame);
            }
        }
    }

    private bool TryConvertFrame([NotNullWhen(true)] out byte[]? rgbData, out int width, out int height)
    {
        rgbData = null;
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
        SwsContext* swsCtx = null;
        byte[]? outputBuffer = null;

        try
        {
            swsCtx = ffmpeg.sws_getContext(
                width, height, (AVPixelFormat)_frame->format,
                width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (swsCtx == null) return ReportFailure("sws_getContext");

            outputBuffer = ArrayPool<byte>.Shared.Rent(checked(rgbLength + RgbTailPadding));
            outputBuffer.AsSpan(rgbLength, RgbTailPadding).Clear();

            int convertedRows;
            // sws_scale is synchronous and does not retain the destination pointer.
            // Write directly into the upload buffer, with no intermediate RGB frame.
            fixed (byte* destination = outputBuffer)
            {
                byte*[] destinationData = [destination, null, null, null];
                int[] destinationStrides = [rgbStride, 0, 0, 0];
                convertedRows = ffmpeg.sws_scale(
                    swsCtx, _frame->data, _frame->linesize, 0, height,
                    destinationData, destinationStrides);
            }
            if (convertedRows != height)
                return ReportFailure("sws_scale", convertedRows);

            // Only the first rgbLength bytes contain tightly packed pixel data.
            rgbData = outputBuffer;
            return true;
        }
        finally
        {
            if (outputBuffer != null && rgbData == null)
                ArrayPool<byte>.Shared.Return(outputBuffer);
            if (swsCtx != null) ffmpeg.sws_freeContext(swsCtx);
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
