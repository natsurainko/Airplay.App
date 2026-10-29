using FFmpeg.AutoGen.Abstractions;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace AirPlay.App.FFmmpeg;

public unsafe partial class H264Decoder : IDisposable
{
    private readonly Lock _lock = new();

    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;

    public bool Disposed { get; private set; }

    public H264Decoder()
    {
        AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null) throw new ApplicationException("Codec not found.");

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null) throw new ApplicationException("Could not allocate codec context.");

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new ApplicationException("Could not open codec.");

        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();
    }

    public bool Decode(byte[] h264Data, [NotNullWhen(true)] out byte[]? rgbData, out int width, out int height)
    {
        rgbData = null;
        width = height = 0;

        if (Disposed) return false;

        lock (_lock)
        {
            fixed (byte* p = h264Data)
            {
                ffmpeg.av_packet_unref(_packet);
                _packet->data = p;
                _packet->size = h264Data.Length;

                int ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (ret < 0) return false;

                ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
                if (ret < 0) return false;

                width = _frame->width;
                height = _frame->height;

                int rgbStride = checked(width * 4);
                int rgbLength = checked(rgbStride * height);

                AVFrame* rgbFrame = ffmpeg.av_frame_alloc();
                if (rgbFrame == null) return false;

                SwsContext* swsCtx = null;
                byte[]? outputBuffer = null;

                try
                {
                    rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
                    rgbFrame->width = width;
                    rgbFrame->height = height;

                    // Let FFmpeg provide aligned storage and its actual row stride.
                    if (ffmpeg.av_frame_get_buffer(rgbFrame, 32) < 0)
                        return false;

                    swsCtx = ffmpeg.sws_getContext(
                        width, height, (AVPixelFormat)_frame->format,
                        width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                        (int)SwsFlags.SWS_BILINEAR, null, null, null);
                    if (swsCtx == null) return false;

                    int convertedRows = ffmpeg.sws_scale(
                        swsCtx,
                        _frame->data, _frame->linesize, 0, height,
                        rgbFrame->data, rgbFrame->linesize);

                    if (convertedRows != height || rgbFrame->linesize[0] < rgbStride)
                        return false;

                    // Win2D receives tightly packed BGRA pixels, without FFmpeg's row padding.
                    outputBuffer = ArrayPool<byte>.Shared.Rent(rgbLength);
                    for (int y = 0; y < height; y++)
                    {
                        new ReadOnlySpan<byte>(
                            rgbFrame->data[0] + y * rgbFrame->linesize[0], rgbStride)
                            .CopyTo(outputBuffer.AsSpan(y * rgbStride, rgbStride));
                    }

                    rgbData = outputBuffer;
                }
                finally
                {
                    if (outputBuffer != null && rgbData == null)
                        ArrayPool<byte>.Shared.Return(outputBuffer);

                    if (swsCtx != null)
                        ffmpeg.sws_freeContext(swsCtx);

                    ffmpeg.av_frame_free(&rgbFrame);
                }

                return true;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Disposed = true;

            if (_frame != null)
            {
                fixed (AVFrame** frame_ptr = &_frame)
                {
                    ffmpeg.av_frame_free(frame_ptr);
                }
            }

            if (_packet != null)
            {
                fixed (AVPacket** packet_ptr = &_packet)
                {
                    ffmpeg.av_packet_free(packet_ptr);
                }
            }

            if (_codecContext != null)
            {
                fixed (AVCodecContext** codecContext_ptr = &_codecContext)
                {
                    ffmpeg.avcodec_free_context(codecContext_ptr);
                }
            }
        }
    }
}
