using System.Runtime.InteropServices;

namespace PabloDock.Native;

internal static class DwmApi
{
    internal const int DwmaCloaked = 14;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hWnd, int attribute, out int value, int valueSize);
}
