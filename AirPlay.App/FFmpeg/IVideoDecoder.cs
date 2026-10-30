using System;
using System.Diagnostics.CodeAnalysis;
using AirPlay.Core2.Models.Messages.Mirror;

namespace AirPlay.App.FFmpeg;

public interface IVideoDecoder : IDisposable
{
    MirrorVideoCodec Codec { get; }

    /// <summary>
    /// Decodes an encoded frame into tightly packed BGRA pixels.
    /// On success, the caller owns rgbData and must return it to ArrayPool&lt;byte&gt;.Shared.
    /// Only width * height * 4 bytes of the rented array contain pixel data.
    /// </summary>
    bool Decode(byte[] encodedData, [NotNullWhen(true)] out byte[]? rgbData, out int width, out int height);
}
