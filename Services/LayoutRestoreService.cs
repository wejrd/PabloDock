using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

public sealed class LayoutRestoreService : IDisposable
{
    private readonly MonitorService monitorService;
    private readonly User32.WinEventProc moveEventCallback;
    private readonly Dictionary<nint, TrackedSnap> trackedSnaps = new();
    private nint moveEventHook;

    private sealed class TrackedSnap(uint processId, CaptureRect visibleBounds, CaptureRect normalBounds)
    {
        public uint ProcessId { get; } = processId;
        public CaptureRect VisibleBounds { get; } = visibleBounds;
        public CaptureRect NormalBounds { get; } = normalBounds;
        public User32.Rect? MoveStartBounds { get; set; }
    }

    public LayoutRestoreService(MonitorService monitorService)
    {
        this.monitorService = monitorService;
        moveEventCallback = OnMoveEvent;
    }

    public void Dispose()
    {
        if (moveEventHook != 0)
        {
            User32.UnhookWinEvent(moveEventHook);
            moveEventHook = 0;
        }

        trackedSnaps.Clear();
    }

    private readonly record struct WindowIdentity(string ProcessName, string ClassName)
    {
        public static WindowIdentity From(CapturedWindow window) =>
            new(window.ProcessName.ToUpperInvariant(), window.ClassName.ToUpperInvariant());

        public static WindowIdentity From(DetectedWindow window) =>
            new(window.ProcessName.ToUpperInvariant(), window.ClassName.ToUpperInvariant());
    }

    private sealed record WindowMatches(
        List<(CapturedWindow Saved, DetectedWindow Running)> Matches,
        List<CapturedWindow> Missing,
        List<CapturedWindow> Ambiguous);

    public async Task<LayoutRestoreResult> RestoreAsync(
        LayoutProfile profile, IReadOnlyList<DetectedWindow> runningWindows,
        WindowEnumerator enumerator)
    {
        const int windowWaitSeconds = 20;
        var current = runningWindows;
        var launched = new List<string>();
        var launchFailed = new List<string>();
        var windowDidNotAppear = new List<string>();
        var includedWindows = profile.Windows.Where(window => window.IncludeInProfile).ToArray();
        foreach (var processGroup in includedWindows.GroupBy(
                     window => window.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var groupWindows = processGroup.ToArray();
            foreach (var saved in groupWindows.Where(window => window.LaunchIfMissing))
            {
                if (!IsMissing(saved))
                {
                    continue;
                }

                var packaged = saved.IsPackaged ||
                    !string.IsNullOrWhiteSpace(saved.ApplicationUserModelId) ||
                    saved.ExecutablePath?.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) == true;
                string? launchTarget = null;
                if (!string.IsNullOrWhiteSpace(saved.CustomLaunchPath) ||
                    !IsProcessRunning(processGroup.Key) ||
                    (packaged && string.IsNullOrWhiteSpace(saved.CustomLaunchPath)))
                {
                    var attempt = ApplicationLaunchService.Launch(saved);
                    if (!attempt.Started)
                    {
                        launchFailed.Add($"{saved.Title} — {processGroup.Key}: {attempt.Error}");
                        continue;
                    }

                    launchTarget = attempt.Target;
                    launched.Add($"{saved.Title} — {processGroup.Key}: {launchTarget}");
                }

                var timeout = Stopwatch.StartNew();
                var appeared = false;
                while (timeout.Elapsed < TimeSpan.FromSeconds(windowWaitSeconds))
                {
                    await Task.Delay(250);
                    current = enumerator.Enumerate();
                    if (!IsMissing(saved))
                    {
                        appeared = true;
                        break;
                    }
                }

                if (!appeared)
                {
                    windowDidNotAppear.Add($"{saved.Title} — {processGroup.Key}: the saved top-level window did not appear within {windowWaitSeconds} seconds" +
                        (launchTarget is null ? "." : $" after launching '{launchTarget}'."));
                }
            }

            bool IsMissing(CapturedWindow window) =>
                FindMissingWindows(groupWindows, current)
                    .Any(missing => ReferenceEquals(missing, window));
        }

        return Restore(profile, current) with
        {
            Launched = launched,
            LaunchFailed = launchFailed,
            WindowDidNotAppear = windowDidNotAppear
        };
    }

    private static IReadOnlyList<CapturedWindow> FindMissingWindows(
        IReadOnlyList<CapturedWindow> savedWindows,
        IReadOnlyList<DetectedWindow> runningWindows)
    {
        var runningByIdentity = runningWindows
            .Where(window => window.ProcessId != (uint)Environment.ProcessId)
            .GroupBy(WindowIdentity.From)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var missing = new List<CapturedWindow>();
        foreach (var savedGroup in savedWindows.GroupBy(WindowIdentity.From))
        {
            runningByIdentity.TryGetValue(savedGroup.Key, out var candidates);
            missing.AddRange(MatchWindows(savedGroup.ToArray(), candidates).Missing);
        }

        return missing;
    }

    private static WindowMatches MatchWindows(
        CapturedWindow[] entries, DetectedWindow[]? candidates)
    {
        var matches = new List<(CapturedWindow Saved, DetectedWindow Running)>();
        var missing = new List<CapturedWindow>();
        var ambiguous = new List<CapturedWindow>();
        if (candidates is null || candidates.Length == 0)
        {
            missing.AddRange(entries);
            return new WindowMatches(matches, missing, ambiguous);
        }

        var savedMatched = new bool[entries.Length];
        var runningMatched = new bool[candidates.Length];

        // Exact titles take priority when this process/class has several windows.
        foreach (var titleGroup in entries.Select((entry, index) => (entry, index))
                     .GroupBy(item => item.entry.Title, StringComparer.Ordinal))
        {
            if (titleGroup.Count() != 1)
            {
                continue;
            }

            var currentWithTitle = candidates.Select((window, index) => (window, index))
                .Where(item => string.Equals(item.window.Title, titleGroup.Key, StringComparison.Ordinal))
                .ToArray();
            if (currentWithTitle.Length != 1)
            {
                continue;
            }

            var savedIndex = titleGroup.First().index;
            var runningIndex = currentWithTitle[0].index;
            savedMatched[savedIndex] = true;
            runningMatched[runningIndex] = true;
            matches.Add((entries[savedIndex], candidates[runningIndex]));
        }

        var remainingSaved = Enumerable.Range(0, entries.Length)
            .Where(index => !savedMatched[index]).ToArray();
        var remainingRunning = Enumerable.Range(0, candidates.Length)
            .Where(index => !runningMatched[index]).ToArray();
        if (remainingSaved.Length == 1 && remainingRunning.Length == 1)
        {
            matches.Add((entries[remainingSaved[0]], candidates[remainingRunning[0]]));
        }
        else if (remainingRunning.Length == 0)
        {
            missing.AddRange(remainingSaved.Select(index => entries[index]));
        }
        else
        {
            ambiguous.AddRange(remainingSaved.Select(index => entries[index]));
        }

        return new WindowMatches(matches, missing, ambiguous);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public LayoutRestoreResult Restore(LayoutProfile profile, IReadOnlyList<DetectedWindow> runningWindows)
    {
        var monitors = monitorService.GetCurrentMonitors();
        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? monitors.FirstOrDefault()
            ?? throw new InvalidOperationException("No active monitor is available.");

        var runningByIdentity = runningWindows
            .Where(window => window.ProcessId != (uint)Environment.ProcessId)
            .GroupBy(WindowIdentity.From)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var missing = new List<string>();
        var ambiguous = new List<string>();
        var failed = new List<string>();
        var restored = 0;

        foreach (var savedGroup in profile.Windows.Where(window => window.IncludeInProfile)
                     .GroupBy(WindowIdentity.From))
        {
            var entries = savedGroup.ToArray();
            runningByIdentity.TryGetValue(savedGroup.Key, out var candidates);
            var resolution = MatchWindows(entries, candidates);
            missing.AddRange(resolution.Missing.Select(entry => entry.Title));
            ambiguous.AddRange(resolution.Ambiguous.Select(entry => entry.Title));

            foreach (var (saved, running) in resolution.Matches)
            {
                var target = SelectTargetMonitor(saved.Monitor, monitors, primary);

                try
                {
                    RestoreWindow(saved, running, target);
                    restored++;
                }
                catch (Exception exception) when (exception is Win32Exception or ArgumentException or InvalidDataException)
                {
                    failed.Add($"{saved.Title}: {exception.Message}");
                }
            }
        }

        return new LayoutRestoreResult(restored, missing, ambiguous, failed);
    }

    private static CapturedMonitor SelectTargetMonitor(
        CapturedMonitor saved, IReadOnlyList<CapturedMonitor> monitors, CapturedMonitor primary)
    {
        // Monitor interface names identify the physical display. GDI names
        // such as DISPLAY1 can be reassigned after a display is powered off.
        if (!string.IsNullOrWhiteSpace(saved.DeviceInterfaceName))
        {
            var byInterface = monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceInterfaceName, saved.DeviceInterfaceName,
                    StringComparison.OrdinalIgnoreCase));
            return byInterface ?? primary;
        }

        // Profiles captured before interface names were stored can still
        // recognize a reassigned display when its desktop bounds are unique.
        var byName = monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.DeviceName, saved.DeviceName,
                StringComparison.OrdinalIgnoreCase));
        var byBounds = monitors.Where(monitor => monitor.Bounds == saved.Bounds).ToArray();
        if (byBounds.Length == 1 && (byName is null || byName.Bounds != saved.Bounds))
        {
            return byBounds[0];
        }

        // The screen can return at its old desktop origin with a different
        // resolution. Use that unique position before a reassigned GDI name.
        var byOrigin = monitors.Where(monitor =>
            monitor.Bounds.X == saved.Bounds.X &&
            monitor.Bounds.Y == saved.Bounds.Y).ToArray();
        if (byOrigin.Length == 1 && (byName is null ||
            byName.Bounds.X != saved.Bounds.X || byName.Bounds.Y != saved.Bounds.Y))
        {
            return byOrigin[0];
        }

        return byName ?? primary;
    }

    private void RestoreWindow(
        CapturedWindow saved, DetectedWindow running, CapturedMonitor target)
    {
        trackedSnaps.Remove(running.Handle);
        var hasDistinctNormalSize = HasDistinctNormalSize(saved);

        // Keep the saved normal rectangle separate from a snapped window's
        // visible rectangle. Minimized GetWindowRect can contain sentinels too.
        var source = !hasDistinctNormalSize && saved.State == WindowState.Normal &&
            IsUsable(saved.CurrentBoundsScreen)
            ? saved.CurrentBoundsScreen
            : saved.Placement.NormalBoundsWorkspace;
        if (!IsUsable(source))
        {
            throw new InvalidDataException("The profile has no usable restored window rectangle.");
        }

        var destination = MapToWorkingArea(source, saved.Monitor.WorkingArea, target.WorkingArea);
        var placement = ReadPlacement(running.Handle);
        placement.Flags = 0;
        placement.ShowCommand = User32.SwShowNormal;
        placement.NormalPosition = ToNativeRect(destination);
        SetPlacement(running.Handle, ref placement);

        // SetWindowPos uses screen coordinates. It puts the restored window on
        // the intended monitor even when workspace and screen origins differ.
        if (!User32.SetWindowPos(running.Handle, 0,
                destination.X, destination.Y, destination.Width, destination.Height,
                User32.SwpNoZOrder | User32.SwpNoActivate))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the window.");
        }

        if (!User32.GetWindowRect(running.Handle, out var actual) ||
            !Intersects(actual, target.WorkingArea))
        {
            throw new Win32Exception("The window did not move onto the target monitor.");
        }

        if (hasDistinctNormalSize)
        {
            var visibleBounds = MapToWorkingArea(
                saved.CurrentBoundsScreen, saved.Monitor.WorkingArea, target.WorkingArea);
            EnsureMoveEventHook();
            // Move silently. Shell Snap shortcuts would show Snap Assist and
            // require the target window to take foreground focus.
            if (!User32.SetWindowPos(running.Handle, 0,
                    visibleBounds.X, visibleBounds.Y, visibleBounds.Width, visibleBounds.Height,
                    User32.SwpNoZOrder | User32.SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not position the snapped window.");
            }

            if (!User32.GetWindowRect(running.Handle, out actual) ||
                !Intersects(actual, target.WorkingArea) || !CloseTo(actual, visibleBounds))
            {
                throw new Win32Exception("The window did not move to its saved geometry.");
            }

            // Direct positioning changes Windows' native normal size. Keep the
            // saved size so dragging this window out can restore it silently.
            trackedSnaps[running.Handle] = new TrackedSnap(
                running.ProcessId, visibleBounds, destination);
            return;
        }

        if (saved.State == WindowState.Normal)
        {
            return;
        }

        // Read back the native workspace rectangle after positioning, then
        // change only the show state. This keeps the usable restored position.
        placement = ReadPlacement(running.Handle);
        placement.Flags = 0;
        placement.ShowCommand = saved.State == WindowState.Maximized
            ? User32.SwShowMaximized
            : User32.SwShowMinimized;
        SetPlacement(running.Handle, ref placement);

        // A running application's old placement can still refer to a monitor
        // that has been disconnected. Keep its restore rectangle on the
        // selected monitor after changing the show state.
        placement = ReadPlacement(running.Handle);
        if (!Intersects(placement.NormalPosition, target.WorkingArea))
        {
            placement.NormalPosition = ToNativeRect(destination);
            SetPlacement(running.Handle, ref placement);
            placement = ReadPlacement(running.Handle);
            if (!Intersects(placement.NormalPosition, target.WorkingArea))
            {
                throw new Win32Exception("The window's restore position remained off-screen.");
            }
        }

        if (saved.State == WindowState.Maximized &&
            (!User32.GetWindowRect(running.Handle, out actual) ||
             !Intersects(actual, target.Bounds)))
        {
            // Re-anchor the window before maximizing if Windows kept an old
            // maximized position on a disconnected monitor.
            placement.ShowCommand = User32.SwShowNormal;
            SetPlacement(running.Handle, ref placement);
            if (!User32.SetWindowPos(running.Handle, 0,
                    destination.X, destination.Y, destination.Width, destination.Height,
                    User32.SwpNoZOrder | User32.SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not move the window onto the target monitor.");
            }

            placement = ReadPlacement(running.Handle);
            placement.ShowCommand = User32.SwShowMaximized;
            SetPlacement(running.Handle, ref placement);
            if (!User32.GetWindowRect(running.Handle, out actual) ||
                !Intersects(actual, target.Bounds))
            {
                throw new Win32Exception("The maximized window remained off-screen.");
            }
        }
    }

    private void EnsureMoveEventHook()
    {
        if (moveEventHook != 0)
        {
            return;
        }

        moveEventHook = User32.SetWinEventHook(
            User32.EventSystemMoveSizeStart, User32.EventSystemMoveSizeEnd,
            0, moveEventCallback, 0, 0,
            User32.WinEventOutOfContext | User32.WinEventSkipOwnProcess);
        if (moveEventHook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not monitor window moves for snapped size restoration.");
        }
    }

    private void OnMoveEvent(nint _, uint eventType, nint handle,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (objectId != 0 || childId != 0 || !trackedSnaps.TryGetValue(handle, out var tracked))
        {
            return;
        }

        try
        {
            User32.GetWindowThreadProcessId(handle, out var processId);
            if (processId != tracked.ProcessId)
            {
                trackedSnaps.Remove(handle);
                return;
            }

            if (eventType == User32.EventSystemMoveSizeStart)
            {
                tracked.MoveStartBounds = User32.GetWindowRect(handle, out var start) &&
                    CloseTo(start, tracked.VisibleBounds) ? start : null;
                return;
            }

            if (eventType != User32.EventSystemMoveSizeEnd ||
                tracked.MoveStartBounds is not { } before ||
                !User32.GetWindowRect(handle, out var after))
            {
                return;
            }

            tracked.MoveStartBounds = null;
            var moved = Math.Abs((long)after.Left - before.Left) > 4 ||
                Math.Abs((long)after.Top - before.Top) > 4;
            var resized = Math.Abs((long)(after.Right - after.Left) -
                (before.Right - before.Left)) > 8 ||
                Math.Abs((long)(after.Bottom - after.Top) -
                (before.Bottom - before.Top)) > 8;
            if (resized || User32.IsIconic(handle) || User32.IsZoomed(handle))
            {
                trackedSnaps.Remove(handle);
                return;
            }

            if (!moved)
            {
                return;
            }

            trackedSnaps.Remove(handle);
            var work = monitorService.GetMonitorForWindow(handle).WorkingArea;
            var width = Math.Min(tracked.NormalBounds.Width, work.Width);
            var height = Math.Min(tracked.NormalBounds.Height, work.Height);
            var x = Math.Clamp(after.Left, work.X, work.X + work.Width - width);
            var y = Math.Clamp(after.Top, work.Y, work.Y + work.Height - height);
            User32.SetWindowPos(handle, 0, x, y, width, height,
                User32.SwpNoZOrder | User32.SwpNoActivate);
        }
        catch (Exception)
        {
            // WinEvent callbacks must not throw into native window handling.
            trackedSnaps.Remove(handle);
        }
    }

    private static bool HasDistinctNormalSize(CapturedWindow saved) =>
        saved.State == WindowState.Normal &&
        IsUsable(saved.CurrentBoundsScreen) &&
        IsUsable(saved.Placement.NormalBoundsWorkspace) &&
        (Math.Abs((long)saved.CurrentBoundsScreen.Width - saved.Placement.NormalBoundsWorkspace.Width) > 32 ||
         Math.Abs((long)saved.CurrentBoundsScreen.Height - saved.Placement.NormalBoundsWorkspace.Height) > 32);

    private static bool CloseTo(User32.Rect actual, CaptureRect expected) =>
        Math.Abs((long)actual.Left - expected.X) <= 32 &&
        Math.Abs((long)actual.Top - expected.Y) <= 32 &&
        Math.Abs((long)actual.Right - expected.X - expected.Width) <= 32 &&
        Math.Abs((long)actual.Bottom - expected.Y - expected.Height) <= 32;

    private static User32.WindowPlacement ReadPlacement(nint handle)
    {
        var placement = new User32.WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<User32.WindowPlacement>()
        };
        if (!User32.GetWindowPlacement(handle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read window placement.");
        }

        return placement;
    }

    private static void SetPlacement(nint handle, ref User32.WindowPlacement placement)
    {
        if (!User32.SetWindowPlacement(handle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set window placement.");
        }
    }

    private static CaptureRect MapToWorkingArea(
        CaptureRect source, CaptureRect savedWork, CaptureRect targetWork)
    {
        if (!IsUsable(savedWork) || !IsUsable(targetWork))
        {
            throw new InvalidDataException("The profile or target monitor has an invalid working area.");
        }

        var scaleX = (double)targetWork.Width / savedWork.Width;
        var scaleY = (double)targetWork.Height / savedWork.Height;
        var width = (int)Math.Clamp(Math.Round(source.Width * scaleX), 1, targetWork.Width);
        var height = (int)Math.Clamp(Math.Round(source.Height * scaleY), 1, targetWork.Height);
        var offsetX = (int)Math.Clamp(
            Math.Round((source.X - (long)savedWork.X) * scaleX), 0, targetWork.Width - width);
        var offsetY = (int)Math.Clamp(
            Math.Round((source.Y - (long)savedWork.Y) * scaleY), 0, targetWork.Height - height);
        var x = targetWork.X + offsetX;
        var y = targetWork.Y + offsetY;

        return new CaptureRect(x, y, width, height);
    }

    private static bool IsUsable(CaptureRect rect) => rect.Width > 0 && rect.Height > 0;

    private static bool Intersects(User32.Rect actual, CaptureRect work) =>
        actual.Right > work.X && actual.Left < (long)work.X + work.Width &&
        actual.Bottom > work.Y && actual.Top < (long)work.Y + work.Height;

    private static User32.Rect ToNativeRect(CaptureRect rect) => new()
    {
        Left = rect.X,
        Top = rect.Y,
        Right = rect.X + rect.Width,
        Bottom = rect.Y + rect.Height
    };
}
