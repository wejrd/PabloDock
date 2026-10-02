using System.ComponentModel;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

public sealed class LayoutCaptureService(
    MonitorService monitorService, ProcessExclusionService exclusionService)
{
    public LayoutProfile Capture(string name, IReadOnlyList<DetectedWindow> windows)
    {
        var captured = new List<CapturedWindow>(windows.Count);
        var excluded = exclusionService.Load().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var window in windows)
        {
            if (window.ProcessId == (uint)Environment.ProcessId ||
                excluded.Contains(window.ProcessName))
            {
                continue;
            }

            var placement = new User32.WindowPlacement
            {
                Length = (uint)Marshal.SizeOf<User32.WindowPlacement>()
            };

            if (!User32.GetWindowPlacement(window.Handle, ref placement))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Could not capture placement for '{window.Title}'. Refresh and try again.");
            }

            var monitor = monitorService.GetMonitorForWindow(window.Handle);
            var bounds = window.Bounds;

            captured.Add(new CapturedWindow(
                window.ProcessName,
                window.ExecutablePath,
                window.Title,
                window.ClassName,
                window.State,
                new CaptureRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                new CapturedPlacement(
                    placement.Flags,
                    placement.ShowCommand,
                    new CapturePoint(placement.MinPosition.X, placement.MinPosition.Y),
                    new CapturePoint(placement.MaxPosition.X, placement.MaxPosition.Y),
                    ToCaptureRect(placement.NormalPosition)),
                monitor,
                User32.GetDpiForWindow(window.Handle))
            {
                IsPackaged = window.IsPackaged,
                ApplicationUserModelId = window.ApplicationUserModelId
            });
        }

        return new LayoutProfile(name, DateTimeOffset.UtcNow, captured);
    }

    private static CaptureRect ToCaptureRect(User32.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
