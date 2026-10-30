using AirPlay.Core2.Models.Messages.Mirror;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;

namespace AirPlay.App.Extensions;

internal static partial class AppLoggers
{
    [LoggerMessage(LogLevel.Critical, "Unhandled process exception; terminating={IsTerminating}")]
    public static partial void UnhandledProcessException(this ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(LogLevel.Critical, "Unhandled UI exception: {Message}")]
    public static partial void UnhandledUiException(this ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Information,
        "Starting AirPlay {Version}; OS={OS}; process={ProcessArchitecture}; OS architecture={OSArchitecture}; " +
        ".NET={Runtime}; SSE2={Sse2}; AVX={Avx}; AVX2={Avx2}; mirror={Width}x{Height}@{MaxFps}; logs={LogDirectory}")]
    public static partial void ApplicationStarting(this ILogger logger, string version, string os,
        Architecture processArchitecture, Architecture osArchitecture, Version runtime,
        bool sse2, bool avx, bool avx2, int width, int height, int maxFps, string logDirectory);

    [LoggerMessage(LogLevel.Information, "FFmpeg bindings initialized; libraries={LibrariesPath}")]
    public static partial void FFmpegBindingsInitialized(this ILogger logger, string librariesPath);

    [LoggerMessage(LogLevel.Critical, "Failed to initialize FFmpeg bindings")]
    public static partial void FFmpegBindingsInitializationFailed(this ILogger logger, Exception exception);
}

internal static partial class MirrorServiceLoggers
{
    [LoggerMessage(LogLevel.Information, "Mirror codec detected for {Device}: {Codec}")]
    public static partial void MirrorCodecDetected(this ILogger logger, string device, MirrorVideoCodec codec);

    [LoggerMessage(LogLevel.Warning, "No mirror processor selected for {Device}: unsupported codec {Codec}")]
    public static partial void MirrorCodecUnsupported(this ILogger logger, string device, MirrorVideoCodec codec);

    [LoggerMessage(LogLevel.Error, "Failed to initialize {Codec} decoder for {Device}")]
    public static partial void MirrorDecoderInitializationFailed(this ILogger logger, Exception exception, string device, MirrorVideoCodec codec);

    [LoggerMessage(LogLevel.Information, "First {Codec} payload for {Device}: {Bytes} bytes")]
    public static partial void FirstMirrorPayload(this ILogger logger, string device, MirrorVideoCodec codec, int bytes);

    [LoggerMessage(LogLevel.Error, "Mirror frame processing terminated for {Device}")]
    public static partial void MirrorFrameProcessingFailed(this ILogger logger, Exception exception, string device);

    [LoggerMessage(LogLevel.Information, "Mirror window created for {Device}: {Width}x{Height}")]
    public static partial void MirrorWindowCreated(this ILogger logger, string device, int width, int height);
}

internal static partial class VideoDecoderLoggers
{
    [LoggerMessage(LogLevel.Information, "Initializing software {Codec} decoder")]
    public static partial void VideoDecoderInitializing(this ILogger logger, MirrorVideoCodec codec);

    [LoggerMessage(LogLevel.Information, "Software {Codec} decoder initialized; avcodec version={Version}")]
    public static partial void VideoDecoderInitialized(this ILogger logger, MirrorVideoCodec codec, uint version);

    [LoggerMessage(LogLevel.Information,
        "First {Codec} decoded frame: {Width}x{Height}, format={Format}, strides={YStride}/{UStride}/{VStride}")]
    public static partial void FirstVideoFrameDecoded(this ILogger logger, MirrorVideoCodec codec, int width, int height,
        AVPixelFormat format, int yStride, int uStride, int vStride);

    [LoggerMessage(LogLevel.Information,
        "{Codec} color conversion: format={Format}, range={Range}, matrix={Matrix}, primaries={Primaries}, " +
        "transfer={Transfer}, chroma={Chroma}; appliedMatrix={AppliedMatrix}, inputFullRange={InputFullRange}, " +
        "matrixFallback={MatrixFallback}, rangeInferred={RangeInferred}; output=full-range BGRA8")]
    public static partial void VideoColorConversionConfigured(this ILogger logger, MirrorVideoCodec codec,
        AVPixelFormat format, AVColorRange range, AVColorSpace matrix, AVColorPrimaries primaries,
        AVColorTransferCharacteristic transfer, AVChromaLocation chroma, string appliedMatrix,
        bool inputFullRange, bool matrixFallback, bool rangeInferred);

    [LoggerMessage(LogLevel.Warning,
        "{Codec} color management is incomplete: matrix={Matrix}, primaries={Primaries}, transfer={Transfer}; " +
        "only supported YUV matrices and ranges are handled; no gamut mapping or HDR tone mapping")]
    public static partial void VideoColorManagementLimited(this ILogger logger, MirrorVideoCodec codec,
        AVColorSpace matrix, AVColorPrimaries primaries, AVColorTransferCharacteristic transfer);

    [LoggerMessage(LogLevel.Warning, "{Codec} pipeline failure: operation={Operation}, result={Result}, failureCount={FailureCount}")]
    public static partial void VideoDecodeFailed(this ILogger logger, MirrorVideoCodec codec, string operation, int? result, int failureCount);
}

internal static partial class MirrorWindowLoggers
{
    [LoggerMessage(LogLevel.Error, "Mirror frame upload failed for {Device}: {Width}x{Height}; failureCount={FailureCount}")]
    public static partial void MirrorFrameUploadFailed(this ILogger logger, Exception exception,
        string device, int width, int height, int failureCount);

    [LoggerMessage(LogLevel.Debug, "Mirror activity for {Device}: FPS={Fps}, dropped={Dropped}")]
    public static partial void MirrorFrameStatistics(this ILogger logger, string device, int fps, int dropped);
}
