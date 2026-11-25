// Licensed to Carbon Monitor under the MIT License.

using System.ComponentModel;
using System.Text;

namespace CarbonMonitor.App.Settings;

/// <summary>
/// Custom TextBox-based control for recording keyboard hotkey combinations.
/// Captures modifier + key combinations via KeyDown events and validates
/// that at least one modifier key (Alt, Ctrl, Shift) is present.
/// </summary>
public sealed class HotkeyRecorder : TextBox
{
    // --- Win32 Modifier flag constants matching RegisterHotKey ---
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private bool _isRecording;
    private uint _recordedModifiers;
    private uint _recordedKey;

    /// <summary>Modifier flags recorded (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public uint RecordedModifiers
    {
        get => _recordedModifiers;
        set
        {
            _recordedModifiers = value;
            UpdateDisplayText();
        }
    }

    /// <summary>Virtual key code of the recorded key (e.g. Keys.R = 0x52).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public uint RecordedKey
    {
        get => _recordedKey;
        set
        {
            _recordedKey = value;
            UpdateDisplayText();
        }
    }

    /// <summary>Whether the control is currently in recording mode.</summary>
    public bool IsRecording => _isRecording;

    /// <summary>Raised when a valid hotkey combination has been recorded.</summary>
    public event EventHandler<HotkeyRecordedEventArgs>? HotkeyRecorded;

    public HotkeyRecorder()
    {
        ReadOnly = true;
        BackColor = System.Drawing.SystemColors.Window;
        TextAlign = HorizontalAlignment.Center;
        Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;

        // Default: Alt + R
        _recordedModifiers = MOD_ALT;
        _recordedKey = (uint)Keys.R;
        UpdateDisplayText();
    }

    /// <summary>Enters hotkey capture mode. The next key combination pressed will be recorded.</summary>
    public void StartRecording()
    {
        _isRecording = true;
        Text = "Press a key combination...";
        BackColor = System.Drawing.Color.FromArgb(255, 255, 240);
        Focus();
    }

    /// <summary>Exits recording mode without changing the stored hotkey.</summary>
    public void StopRecording()
    {
        _isRecording = false;
        BackColor = System.Drawing.SystemColors.Window;
        UpdateDisplayText();
    }

    /// <summary>Sets the displayed hotkey from modifier flags and a virtual key code.</summary>
    public void SetHotkey(uint modifiers, uint key)
    {
        _recordedModifiers = modifiers;
        _recordedKey = key;
        UpdateDisplayText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_isRecording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        // Ignore standalone modifier presses—wait for a non-modifier key.
        if (IsModifierKey(e.KeyCode))
        {
            var previewSb = new StringBuilder();
            if (e.Control) previewSb.Append("Ctrl + ");
            if (e.Alt) previewSb.Append("Alt + ");
            if (e.Shift) previewSb.Append("Shift + ");
            previewSb.Append("...");
            Text = previewSb.ToString();
            return;
        }

        // Build modifier flags.
        uint modifiers = 0;
        if (e.Control) modifiers |= MOD_CONTROL;
        if (e.Alt) modifiers |= MOD_ALT;
        if (e.Shift) modifiers |= MOD_SHIFT;

        // Validate: must have at least one modifier.
        if (modifiers == 0)
        {
            Text = "Must include Alt, Ctrl, or Shift!";
            return;
        }

        // Accept the combination.
        _recordedModifiers = modifiers;
        _recordedKey = (uint)e.KeyCode;
        _isRecording = false;
        BackColor = System.Drawing.SystemColors.Window;
        UpdateDisplayText();

        HotkeyRecorded?.Invoke(this, new HotkeyRecordedEventArgs(_recordedModifiers, _recordedKey));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (_isRecording)
        {
            StopRecording();
        }
    }

    // Prevent the TextBox from processing Enter/Escape while recording.
    protected override bool IsInputKey(Keys keyData)
    {
        if (_isRecording) return true;
        return base.IsInputKey(keyData);
    }

    // Prevent Alt from activating the menu strip.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_isRecording) return true;
        return base.ProcessDialogKey(keyData);
    }

    private void UpdateDisplayText()
    {
        Text = FormatHotkey(_recordedModifiers, _recordedKey);
    }

    /// <summary>Formats modifier flags and a virtual key into a human-readable string such as "Alt + R".</summary>
    public static string FormatHotkey(uint modifiers, uint key)
    {
        var sb = new StringBuilder();
        if ((modifiers & MOD_CONTROL) != 0) sb.Append("Ctrl + ");
        if ((modifiers & MOD_ALT) != 0) sb.Append("Alt + ");
        if ((modifiers & MOD_SHIFT) != 0) sb.Append("Shift + ");

        var keyCode = (Keys)key;
        sb.Append(FormatKeyName(keyCode));
        return sb.ToString();
    }

    private static string FormatKeyName(Keys key)
    {
        return key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.A and <= Keys.Z => key.ToString(),
            >= Keys.F1 and <= Keys.F24 => key.ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Oemtilde => "`",
            Keys.Space => "Space",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Escape => "Escape",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Enter or Keys.Return => "Enter",
            Keys.PrintScreen => "PrintScreen",
            Keys.Scroll => "ScrollLock",
            Keys.Pause => "Pause",
            Keys.Multiply => "Num*",
            Keys.Add => "Num+",
            Keys.Subtract => "Num-",
            Keys.Decimal => "Num.",
            Keys.Divide => "Num/",
            _ => key.ToString()
        };
    }

    private static bool IsModifierKey(Keys key)
    {
        return key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu;
    }
}

/// <summary>Event data for <see cref="HotkeyRecorder.HotkeyRecorded"/>.</summary>
public sealed class HotkeyRecordedEventArgs : EventArgs
{
    public uint Modifiers { get; }
    public uint Key { get; }

    public HotkeyRecordedEventArgs(uint modifiers, uint key)
    {
        Modifiers = modifiers;
        Key = key;
    }
}
