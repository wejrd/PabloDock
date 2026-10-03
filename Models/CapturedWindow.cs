namespace PabloDock.Models;

public sealed record CaptureRect(int X, int Y, int Width, int Height);

public sealed record CapturePoint(int X, int Y);

public sealed record CapturedPlacement(
    uint Flags,
    uint ShowCommand,
    CapturePoint MinPosition,
    CapturePoint MaxPosition,
    CaptureRect NormalBoundsWorkspace);

public sealed record CapturedMonitor(
    string DeviceName,
    bool IsPrimary,
    CaptureRect Bounds,
    CaptureRect WorkingArea)
{
    public string? DeviceInterfaceName { get; init; }
}

public sealed record CapturedWindow(
    string ProcessName,
    string? ExecutablePath,
    string Title,
    string ClassName,
    WindowState State,
    CaptureRect CurrentBoundsScreen,
    CapturedPlacement Placement,
    CapturedMonitor Monitor,
    uint WindowDpi)
{
    public bool IsPackaged { get; init; }
    public string? ApplicationUserModelId { get; init; }
    public string? CustomLaunchPath { get; init; }
    public string? CustomLaunchArguments { get; init; }
    public bool IncludeInProfile { get; init; } = true;
    public bool LaunchIfMissing { get; init; } = true;
}
