namespace PabloDock.Models;

public sealed record StartupSettings
{
    public bool StartWithWindows { get; init; }
    public bool RestoreProfileAfterStartup { get; init; }
    public string? ProfileName { get; init; }
}
