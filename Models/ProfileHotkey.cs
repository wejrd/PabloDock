namespace PabloDock.Models;

public sealed record ProfileHotkey(
    uint VirtualKey,
    bool Ctrl,
    bool Alt,
    bool Shift,
    bool Win)
{
    public static bool IsSupportedKey(uint key) =>
        key is >= 0x41 and <= 0x5A or
            >= 0x30 and <= 0x39 or
            >= 0x60 and <= 0x69 or
            >= 0x70 and <= 0x87;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(VirtualKey switch
        {
            >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)VirtualKey).ToString(),
            >= 0x60 and <= 0x69 => $"Num {VirtualKey - 0x60}",
            >= 0x70 and <= 0x87 => $"F{VirtualKey - 0x70 + 1}",
            _ => $"Key {VirtualKey}"
        });
        return string.Join(" + ", parts);
    }
}
