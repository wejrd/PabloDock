using System.ComponentModel;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

internal sealed class GlobalHotkeyService(nint windowHandle) : IDisposable
{
    private sealed record Registration(int Id, ProfileHotkey Hotkey);

    private readonly Dictionary<string, Registration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 1;

    public string? GetProfileName(int id) =>
        _registrations.FirstOrDefault(item => item.Value.Id == id).Key;

    public void RenameProfile(string currentName, string newName)
    {
        if (_registrations.Remove(currentName, out var registration))
        {
            _registrations.Add(newName, registration);
        }
    }

    public bool TrySet(string profileName, ProfileHotkey? hotkey, out string? error)
    {
        error = null;
        _registrations.TryGetValue(profileName, out var previous);
        if (previous?.Hotkey == hotkey)
        {
            return true;
        }

        if (hotkey is not null)
        {
            if (!ProfileHotkey.IsSupportedKey(hotkey.VirtualKey))
            {
                error = $"{hotkey} is not a supported hotkey.";
                return false;
            }

            var duplicate = _registrations.FirstOrDefault(item =>
                !string.Equals(item.Key, profileName, StringComparison.OrdinalIgnoreCase) &&
                item.Value.Hotkey == hotkey);
            if (duplicate.Key is not null)
            {
                error = $"{hotkey} is already assigned to profile '{duplicate.Key}'.";
                return false;
            }

            var id = _nextId++;
            if (!User32.RegisterHotKey(windowHandle, id, ToNativeModifiers(hotkey),
                    hotkey.VirtualKey))
            {
                var win32Error = Marshal.GetLastWin32Error();
                error = $"Windows could not register {hotkey} for '{profileName}'. " +
                    $"Another application may already use it. {new Win32Exception(win32Error).Message} " +
                    $"(error {win32Error}).";
                return false;
            }

            _registrations[profileName] = new Registration(id, hotkey);
        }
        else
        {
            _registrations.Remove(profileName);
        }

        if (previous is not null)
        {
            User32.UnregisterHotKey(windowHandle, previous.Id);
        }

        return true;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations.Values)
        {
            User32.UnregisterHotKey(windowHandle, registration.Id);
        }

        _registrations.Clear();
    }

    private static uint ToNativeModifiers(ProfileHotkey hotkey) =>
        User32.ModNoRepeat |
        (hotkey.Ctrl ? User32.ModControl : 0) |
        (hotkey.Alt ? User32.ModAlt : 0) |
        (hotkey.Shift ? User32.ModShift : 0) |
        (hotkey.Win ? User32.ModWin : 0);
}
