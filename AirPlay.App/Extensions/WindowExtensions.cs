using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using WinRT.Interop;

namespace AirPlay.App.Extensions;

internal static class WindowExtensions
{
    public static void SetFocus(this Window window) => PInvoke.SetFocus(new HWND(WindowNative.GetWindowHandle(window)));

    public static int GetRefreshRate(this Window window)
    {
        HDC? hDC = null;
        HWND hWND = new(WindowNative.GetWindowHandle(window));

        try
        {
            hDC = PInvoke.GetDC(hWND);
            return PInvoke.GetDeviceCaps(hDC.Value, GET_DEVICE_CAPS_INDEX.VREFRESH);
        }
        catch
        {
            return 60;
        }
        finally
        {
            if (hDC != null)
                PInvoke.ReleaseDC(hWND, hDC.Value);
        }
    }
}
