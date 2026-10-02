using System.Drawing;

namespace PabloDock.Models;

public enum WindowState
{
    Normal,
    Minimized,
    Maximized
}

public sealed record DetectedWindow(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string Title,
    string ClassName,
    Rectangle Bounds,
    WindowState State)
{
    public bool IsPackaged { get; init; }
    public string? ApplicationUserModelId { get; init; }
}
