using FFmpeg.AutoGen.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;

namespace AirPlay.App;

public unsafe partial class H264Decoder : IDisposable
{
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVPacket* _packet;

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

            int rgbStride = width * 4;
            rgbData = new byte[rgbStride * height];

            AVFrame* rgbFrame = ffmpeg.av_frame_alloc();
            try
            {
                rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
                rgbFrame->width = width;
                rgbFrame->height = height;

                fixed (byte* prgb = rgbData)
                {
                    rgbFrame->data[0] = prgb;
                    rgbFrame->linesize[0] = rgbStride;

                    SwsContext* swsCtx = ffmpeg.sws_getContext(
                        width, height, (AVPixelFormat)_frame->format,
                        width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                        ffmpeg.SWS_FAST_BILINEAR, null, null, null);

                    ffmpeg.sws_scale(
                        swsCtx,
                        _frame->data, _frame->linesize, 0, height,
                        rgbFrame->data, rgbFrame->linesize);

                    ffmpeg.sws_freeContext(swsCtx);
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&rgbFrame);
            }

            return true;
        }
    }

    public void Dispose()
    {
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