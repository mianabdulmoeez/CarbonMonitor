using System.Runtime.InteropServices;
using CarbonMonitor.Core.Configuration;

namespace CarbonMonitor.App.Hotkey;

/// <summary>
/// Registers and handles global hotkeys using the Win32 RegisterHotKey API (no low-level keyboard hook).
/// Detects registration conflicts and supports clean recovery/reset.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xCB01;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _windowHandle;
    private bool _registered;

    public bool IsRegistered => _registered;
    public string? ConflictMessage { get; private set; }
    public uint CurrentModifiers { get; private set; }
    public uint CurrentKey { get; private set; }

    public event Action? HotkeyPressed;

    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public bool TryRegister(uint modifiers, uint key)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            ConflictMessage = "Hotkey window handle is not initialized.";
            return false;
        }

        if (!VirtualKeyHelper.IsValidHotkey((int)modifiers, (int)key))
        {
            ConflictMessage = "Shortcut must include at least one modifier (Alt, Ctrl, Shift, or Win) and a non-modifier key.";
            return false;
        }

        Unregister();

        // MOD_NOREPEAT (0x4000) prevents key-repeat spam when holding the hotkey down
        const uint modNoRepeat = 0x4000;
        _registered = RegisterHotKey(_windowHandle, HotkeyId, modifiers | modNoRepeat, key);
        if (!_registered)
        {
            // Retry without MOD_NOREPEAT just in case
            _registered = RegisterHotKey(_windowHandle, HotkeyId, modifiers, key);
        }

        if (!_registered)
        {
            int error = Marshal.GetLastWin32Error();
            var label = VirtualKeyHelper.FormatHotkey((int)modifiers, (int)key);
            ConflictMessage = error == ErrorHotkeyAlreadyRegistered
                ? $"Shortcut '{label}' is already in use by another application (Win32 error 1409). Open Settings > Hotkey to choose another combination or reset."
                : $"Could not register shortcut '{label}' (Win32 error {error}).";
            return false;
        }

        CurrentModifiers = modifiers;
        CurrentKey = key;
        ConflictMessage = null;
        return true;
    }

    public bool ResetToDefault()
    {
        return TryRegister(VirtualKeyHelper.ModAlt, 82); // Alt + R
    }

    public void Unregister()
    {
        if (_registered && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }
    }

    public bool ProcessMessage(int msg, IntPtr wParam)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return true;
        }
        return false;
    }

    public void Dispose() => Unregister();
}
