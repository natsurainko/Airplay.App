using AirPlay.Core2.Models.Messages.Mirror;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;

namespace AirPlay.App.FFmpeg;

public sealed class H264Decoder(ILogger<H264Decoder> logger)
    : FFmpegVideoDecoder(MirrorVideoCodec.H264, AVCodecID.AV_CODEC_ID_H264, logger)
{
}
