namespace PabloDock.Models;

public sealed record LayoutRestoreResult(
    int Restored,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Ambiguous,
    IReadOnlyList<string> Failed)
{
    public IReadOnlyList<string> Launched { get; init; } = [];
    public IReadOnlyList<string> LaunchFailed { get; init; } = [];
    public IReadOnlyList<string> WindowDidNotAppear { get; init; } = [];
}
