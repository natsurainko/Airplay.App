using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;

namespace AirPlay.App.Extensions;

internal static class WindowExtensions
{
    public static void SetFocus(this Window window) => PInvoke.SetFocus(new HWND(WindowNative.GetWindowHandle(window)));
}
