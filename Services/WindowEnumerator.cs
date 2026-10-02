using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

public sealed class WindowEnumerator
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public IReadOnlyList<DetectedWindow> Enumerate()
    {
        var windows = new List<DetectedWindow>();

        bool VisitWindow(nint handle, nint _)
        {
            var window = TryReadWindow(handle);
            if (window is not null)
            {
                windows.Add(window);
            }

            return true;
        }

        if (!User32.EnumWindows(VisitWindow, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate desktop windows.");
        }

        return windows;
    }

    private static DetectedWindow? TryReadWindow(nint handle)
    {
        if (!User32.IsWindowVisible(handle) || IsCloaked(handle))
        {
            return null;
        }

        var titleLength = User32.GetWindowTextLength(handle);
        if (titleLength == 0)
        {
            return null;
        }

        var titleBuffer = new StringBuilder(titleLength + 1);
        if (User32.GetWindowText(handle, titleBuffer, titleBuffer.Capacity) == 0)
        {
            return null;
        }

        var title = titleBuffer.ToString();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var classBuffer = new StringBuilder(256);
        User32.GetClassName(handle, classBuffer, classBuffer.Capacity);
        var className = classBuffer.ToString();
        if (ShellClasses.Contains(className))
        {
            return null;
        }

        var extendedStyle = User32.GetWindowLongPtr(handle, User32.GwlExStyle).ToInt64();
        if ((extendedStyle & User32.WsExToolWindow) != 0 &&
            (extendedStyle & User32.WsExAppWindow) == 0)
        {
            return null;
        }

        if (!User32.GetWindowRect(handle, out var rect))
        {
            return null;
        }

        User32.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return null;
        }

        var (processName, executablePath) = GetProcessInfo(processId);
        var (isPackaged, applicationUserModelId) = ApplicationIdentityService.Read(processId);
        if (isPackaged && applicationUserModelId is null)
        {
            applicationUserModelId = ApplicationIdentityService.ResolveAumidFromPackagedPath(
                executablePath);
        }
        var state = User32.IsIconic(handle) ? WindowState.Minimized
            : User32.IsZoomed(handle) ? WindowState.Maximized
            : WindowState.Normal;

        return new DetectedWindow(
            handle,
            processId,
            processName,
            executablePath,
            title,
            className,
            Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
            state)
        {
            IsPackaged = isPackaged,
            ApplicationUserModelId = applicationUserModelId
        };
    }

    private static bool IsCloaked(nint handle) =>
        DwmApi.DwmGetWindowAttribute(handle, DwmApi.DwmaCloaked, out var cloaked, sizeof(int)) == 0
        && cloaked != 0;

    private static (string Name, string? ExecutablePath) GetProcessInfo(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var name = process.ProcessName;

            try
            {
                return (name, process.MainModule?.FileName);
            }
            catch (Win32Exception)
            {
                return (name, null);
            }
            catch (InvalidOperationException)
            {
                return (name, null);
            }
        }
        catch (ArgumentException)
        {
            return ("Unknown", null);
        }
        catch (InvalidOperationException)
        {
            return ("Unknown", null);
        }
        catch (Win32Exception)
        {
            return ("Unknown", null);
        }
    }
}
