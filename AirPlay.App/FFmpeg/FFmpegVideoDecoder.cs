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
    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private int _failureCount;
    private bool _loggedFirstFrame;
    private bool _draining;
    private bool _endOfStream;
    private (AVPixelFormat Format, AVColorRange Range, AVColorSpace Matrix,
        AVColorPrimaries Primaries, AVColorTransferCharacteristic Transfer,
        AVChromaLocation Chroma)? _lastColorMetadata;

    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVFrame* _rgbFrame;
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
            _rgbFrame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_frame == null || _rgbFrame == null || _packet == null)
                throw new ApplicationException("Could not allocate decoder frames or packet.");

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
        if (!EnsureRgbFrame(width, height)) return false;
        AVFrame* rgbFrame = _rgbFrame;
        SwsContext* swsCtx = null;
        byte[]? outputBuffer = null;

        try
        {
            var sourceFormat = (AVPixelFormat)_frame->format;
            // YUVJ has the same plane layout as YUV, but implies full range. Normalize
            // the format so range is controlled explicitly below, not implicitly by swscale.
            swsCtx = ffmpeg.sws_getContext(
                width, height, NormalizeYuvFormat(sourceFormat),
                width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (swsCtx == null) return ReportFailure("sws_getContext");
            if (!ConfigureColorspace(swsCtx, sourceFormat)) return false;

            int convertedRows = ffmpeg.sws_scale(
                swsCtx, _frame->data, _frame->linesize, 0, height,
                rgbFrame->data, rgbFrame->linesize);
            if (convertedRows != height || rgbFrame->linesize[0] < rgbStride)
                return ReportFailure("sws_scale", convertedRows);

            // Win2D expects tightly packed pixels. Copy only the visible bytes of each
            // aligned output row, excluding FFmpeg's per-row padding.
            outputBuffer = ArrayPool<byte>.Shared.Rent(rgbLength);
            for (int y = 0; y < height; y++)
            {
                new ReadOnlySpan<byte>(rgbFrame->data[0] + y * rgbFrame->linesize[0], rgbStride)
                    .CopyTo(outputBuffer.AsSpan(y * rgbStride, rgbStride));
            }
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

    // Called under _lock. Only the independent managed copy escapes to the renderer,
    // so the aligned native destination can be overwritten for the next decoded frame.
    private bool EnsureRgbFrame(int width, int height)
    {
        if (_rgbFrame->buf[0] == null ||
            _rgbFrame->format != (int)AVPixelFormat.AV_PIX_FMT_BGRA ||
            _rgbFrame->width != width || _rgbFrame->height != height)
        {
            ffmpeg.av_frame_unref(_rgbFrame);
            _rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            _rgbFrame->width = width;
            _rgbFrame->height = height;
            int result = ffmpeg.av_frame_get_buffer(_rgbFrame, 32);
            if (result < 0)
            {
                // Leave an empty frame so the next call can retry even at the same size.
                ffmpeg.av_frame_unref(_rgbFrame);
                return ReportFailure("av_frame_get_buffer", result);
            }
            return true;
        }

        // Normally already exclusive: no new allocation occurs in that case.
        int writableResult = ffmpeg.av_frame_make_writable(_rgbFrame);
        return writableResult >= 0 || ReportFailure("av_frame_make_writable", writableResult);
    }

    private static AVPixelFormat NormalizeYuvFormat(AVPixelFormat format) => format switch
    {
        AVPixelFormat.AV_PIX_FMT_YUVJ420P => AVPixelFormat.AV_PIX_FMT_YUV420P,
        AVPixelFormat.AV_PIX_FMT_YUVJ422P => AVPixelFormat.AV_PIX_FMT_YUV422P,
        AVPixelFormat.AV_PIX_FMT_YUVJ444P => AVPixelFormat.AV_PIX_FMT_YUV444P,
        AVPixelFormat.AV_PIX_FMT_YUVJ440P => AVPixelFormat.AV_PIX_FMT_YUV440P,
        AVPixelFormat.AV_PIX_FMT_YUVJ411P => AVPixelFormat.AV_PIX_FMT_YUV411P,
        _ => format
    };

    private bool ConfigureColorspace(SwsContext* swsCtx, AVPixelFormat sourceFormat)
    {
        bool rangeInferred = _frame->color_range is not
            (AVColorRange.AVCOL_RANGE_JPEG or AVColorRange.AVCOL_RANGE_MPEG);
        bool fullRange = _frame->color_range switch
        {
            AVColorRange.AVCOL_RANGE_JPEG => true,
            AVColorRange.AVCOL_RANGE_MPEG => false,
            // With no explicit range, legacy YUVJ implies full range; other YUV defaults to limited.
            _ => NormalizeYuvFormat(sourceFormat) != sourceFormat
        };

        var (coefficientId, matrixName, matrixFallback) = _frame->colorspace switch
        {
            AVColorSpace.AVCOL_SPC_BT709 => (ffmpeg.SWS_CS_ITU709, "BT.709", false),
            AVColorSpace.AVCOL_SPC_BT470BG or AVColorSpace.AVCOL_SPC_SMPTE170M
                => (ffmpeg.SWS_CS_ITU601, "BT.601", false),
            AVColorSpace.AVCOL_SPC_FCC => (ffmpeg.SWS_CS_FCC, "FCC", false),
            AVColorSpace.AVCOL_SPC_SMPTE240M => (ffmpeg.SWS_CS_SMPTE240M, "SMPTE240M", false),
            AVColorSpace.AVCOL_SPC_BT2020_NCL => (ffmpeg.SWS_CS_BT2020, "BT.2020 NCL", false),
            // Application-specific fallback for untagged AirPlay screen mirroring, not a
            // general rule for untagged video. Keep it independent of portrait/landscape size.
            // Unsupported matrices are also approximated and produce a warning below.
            _ => (ffmpeg.SWS_CS_ITU709, "BT.709", true)
        };

        int* coefficientTable = ffmpeg.sws_getCoefficients(coefficientId);
        if (coefficientTable == null) return ReportFailure("sws_getCoefficients");
        int4 coefficients = default;
        for (uint i = 0; i < 4; i++) coefficients[i] = coefficientTable[i];

        // RGB output uses full-range code values. The second table does not perform a
        // primaries/gamut conversion; it is the matrix for YUV output (unused for BGRA).
        // Neutral 16.16 corrections: brightness=0, contrast=1, saturation=1.
        int result = ffmpeg.sws_setColorspaceDetails(
            swsCtx, in coefficients, fullRange ? 1 : 0, in coefficients, 1,
            0, 1 << 16, 1 << 16);
        if (result < 0) return ReportFailure("sws_setColorspaceDetails", result);

        var metadata = (sourceFormat, _frame->color_range, _frame->colorspace,
            _frame->color_primaries, _frame->color_trc, _frame->chroma_location);
        if (!_lastColorMetadata.HasValue || !_lastColorMetadata.Value.Equals(metadata))
        {
            _logger.VideoColorConversionConfigured(Codec, sourceFormat, _frame->color_range,
                _frame->colorspace, _frame->color_primaries, _frame->color_trc,
                _frame->chroma_location, matrixName, fullRange, matrixFallback, rangeInferred);

            bool hdr = _frame->color_trc is AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084
                or AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67;
            bool otherPrimaries = _frame->color_primaries is not
                (AVColorPrimaries.AVCOL_PRI_UNSPECIFIED or AVColorPrimaries.AVCOL_PRI_BT709);
            bool unsupportedMatrix = matrixFallback && _frame->colorspace != AVColorSpace.AVCOL_SPC_UNSPECIFIED;
            if (hdr || otherPrimaries || unsupportedMatrix)
                _logger.VideoColorManagementLimited(Codec, _frame->colorspace,
                    _frame->color_primaries, _frame->color_trc);

            _lastColorMetadata = metadata;
        }
        return true;
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
            if (_rgbFrame != null)
            {
                fixed (AVFrame** rgbFrame = &_rgbFrame) ffmpeg.av_frame_free(rgbFrame);
            }
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
