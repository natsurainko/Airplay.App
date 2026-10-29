using AirPlay.Core2.Models.Configs;
using Windows.Storage;

namespace AirPlay.App.Configurations;

// An immutable snapshot for this application run. Saving menu selections does not change it.
public sealed class AppConfiguration
{
    private const string ResolutionKey = "Mirror.ResolutionHeight";
    private const string FrameRateKey = "Mirror.MaxFps";

    public int MirrorWidth { get; }
    public int MirrorHeight { get; }
    public int MirrorMaxFps { get; }

    private AppConfiguration(int height, int maxFps)
    {
        MirrorWidth = height switch
        {
            1080 => 1920,
            2160 => 3840,
            _ => 2560
        };
        MirrorHeight = height;
        MirrorMaxFps = maxFps;
    }

    public static AppConfiguration Load()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var defaults = new AirTunesConfig();

        int height = values[ResolutionKey] is int savedHeight && savedHeight is 1080 or 1440 or 2160
            ? savedHeight : defaults.MirrorHeight;
        int maxFps = values[FrameRateKey] is int savedFps && savedFps is 30 or 60 or 120
            ? savedFps : defaults.MirrorMaxFps;

        return new AppConfiguration(height, maxFps);
    }

    public static void SaveMirrorHeight(int height)
    {
        if (height is not (1080 or 1440 or 2160))
            throw new System.ArgumentOutOfRangeException(nameof(height));

        ApplicationData.Current.LocalSettings.Values[ResolutionKey] = height;
    }

    public static void SaveMirrorMaxFps(int maxFps)
    {
        if (maxFps is not (30 or 60 or 120))
            throw new System.ArgumentOutOfRangeException(nameof(maxFps));

        ApplicationData.Current.LocalSettings.Values[FrameRateKey] = maxFps;
    }
}
