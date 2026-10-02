using System.ComponentModel;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

public sealed class MonitorService
{
    public CapturedMonitor GetMonitorForWindow(nint windowHandle)
    {
        var monitorHandle = User32.MonitorFromWindow(windowHandle, User32.MonitorDefaultToNearest);
        if (monitorHandle == 0)
        {
            throw new Win32Exception("Could not determine the window's monitor.");
        }

        return ReadMonitor(monitorHandle);
    }

    public IReadOnlyList<CapturedMonitor> GetCurrentMonitors()
    {
        var handles = new List<nint>();
        bool AddMonitor(nint monitor, nint _, nint __, nint ___)
        {
            handles.Add(monitor);
            return true;
        }

        if (!User32.EnumDisplayMonitors(0, 0, AddMonitor, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate monitors.");
        }

        return handles.Select(ReadMonitor).ToArray();
    }

    private static CapturedMonitor ReadMonitor(nint monitorHandle)
    {
        var info = new User32.MonitorInfoEx
        {
            Size = (uint)Marshal.SizeOf<User32.MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        if (!User32.GetMonitorInfo(monitorHandle, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read monitor information.");
        }

        return new CapturedMonitor(
            info.DeviceName,
            (info.Flags & User32.MonitorInfoPrimary) != 0,
            ToCaptureRect(info.Monitor),
            ToCaptureRect(info.Work));
    }

    private static CaptureRect ToCaptureRect(User32.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
