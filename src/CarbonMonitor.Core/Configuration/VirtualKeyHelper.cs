using System.Text;

namespace CarbonMonitor.Core.Configuration;

/// <summary>
/// Pure-.NET helper for formatting Win32 virtual key codes and modifier flags without WinForms dependency.
/// </summary>
public static class VirtualKeyHelper
{
    public const int ModAlt = 1;
    public const int ModControl = 2;
    public const int ModShift = 4;
    public const int ModWin = 8;

    public static string FormatHotkey(int modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(FormatVirtualKey(virtualKey));
        return string.Join(" + ", parts);
    }

    public static string FormatVirtualKey(int vk)
    {
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();

        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();

        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x70 + 1}";

        if (vk is >= 0x60 and <= 0x69)
            return $"Num{vk - 0x60}";

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"Key(0x{vk:X2})"
        };
    }

    public static bool IsValidHotkey(int modifiers, int virtualKey)
    {
        // Require at least one modifier and a non-modifier virtual key
        if ((modifiers & (ModAlt | ModControl | ModShift | ModWin)) == 0)
            return false;

        if (virtualKey <= 0)
            return false;

        // Disallow bare modifier keys
        return virtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
    }
}
