using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using System;
using System.IO;
using Windows.ApplicationModel;

namespace AirPlay.App.FFmpeg;
public class FFmpegBinariesHelper
{
    internal static void RegisterFFmpegBinaries()
    {
        var current = Environment.CurrentDirectory;
        var probe = Path.Combine(Package.Current.InstalledPath, "Libraries");

        DynamicallyLoadedBindings.LibrariesPath = Path.Combine(Package.Current.InstalledPath, "Libraries");
    }
}