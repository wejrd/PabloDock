namespace PabloDock.Models;

public sealed record LayoutProfile(
    string Name,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<CapturedWindow> Windows)
{
    public ProfileHotkey? Hotkey { get; init; }
}
