using AirPlay.Core2.Models.Messages.Mirror;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;

namespace AirPlay.App.FFmpeg;

/// <summary>Experimental HEVC software decoder for AirPlay mirroring.</summary>
public sealed class HevcDecoder(ILogger<HevcDecoder> logger)
    : FFmpegVideoDecoder(MirrorVideoCodec.Hevc, AVCodecID.AV_CODEC_ID_HEVC, logger)
{
}
