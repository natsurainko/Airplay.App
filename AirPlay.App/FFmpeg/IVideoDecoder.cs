using System;
using AirPlay.Core2.Models.Messages.Mirror;

namespace AirPlay.App.FFmpeg;

public interface IVideoDecoder : IDisposable
{
    MirrorVideoCodec Codec { get; }

    /// <summary>
    /// Submits an encoded access unit and receives all currently available frames.
    /// onFrame is called synchronously zero or more times, in decoded output order.
    /// Each callback takes ownership of its rented BGRA array, including if it throws,
    /// and must eventually return it to ArrayPool&lt;byte&gt;.Shared.
    /// Only width * height * 4 bytes contain tightly packed pixel data.
    /// Callbacks must not reenter Decode, Flush or Dispose on this decoder.
    /// </summary>
    void Decode(byte[] encodedData, Action<byte[], int, int> onFrame);

    /// <summary>
    /// Signals end of input and delivers delayed frames using the same callback contract.
    /// Decode cannot be called afterwards; create a new decoder for a new stream.
    /// Dispose deliberately discards delayed frames instead of invoking callbacks.
    /// </summary>
    void Flush(Action<byte[], int, int> onFrame);
}
