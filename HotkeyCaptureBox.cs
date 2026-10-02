using System.ComponentModel;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock;

internal sealed class HotkeyCaptureBox : TextBox
{
    private readonly User32.LowLevelKeyboardProc _callback;
    private nint _hook;
    private ProfileHotkey? _hotkey;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProfileHotkey? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Text = value?.ToString() ?? "Click here, then press a combination";
        }
    }

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        _callback = OnKeyboardEvent;
        Hotkey = null;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _hook = User32.SetWindowsHookEx(User32.WhKeyboardLl, _callback,
            User32.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            MessageBox.Show(FindForm(),
                $"Could not capture system key combinations: {new Win32Exception(error).Message}",
                "Hotkey capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        StopCapture();
        base.OnLostFocus(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopCapture();
        }

        base.Dispose(disposing);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_hook == 0 && ProfileHotkey.IsSupportedKey((uint)e.KeyCode))
        {
            SetCapturedHotkey((uint)e.KeyCode);
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        if (code >= 0 && ContainsFocus &&
            (message == User32.WmKeyDown || message == User32.WmSysKeyDown))
        {
            var key = (uint)Marshal.ReadInt32(data);
            if (ProfileHotkey.IsSupportedKey(key))
            {
                SetCapturedHotkey(key);
                return 1;
            }
        }

        return User32.CallNextHookEx(_hook, code, message, data);
    }

    private void SetCapturedHotkey(uint key)
    {
        static bool IsDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        Hotkey = new ProfileHotkey(key,
            IsDown(0x11), IsDown(0x12), IsDown(0x10), IsDown(0x5B) || IsDown(0x5C));
    }

    private void StopCapture()
    {
        if (_hook != 0)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }
}
