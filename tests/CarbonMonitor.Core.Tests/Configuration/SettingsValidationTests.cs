using CarbonMonitor.Core.Configuration;
using Xunit;

namespace CarbonMonitor.Core.Tests.Configuration;

public class SettingsValidationTests
{
    [Fact]
    public void ValidateAndMigrate_ClampsUnsafeBounds()
    {
        var raw = new AppSettings
        {
            ConfigVersion = 0,
            PollingIntervalMs = 10,
            ProviderTimeoutMs = 999999,
            AlertCooldownSeconds = 1,
            LogRetentionDays = 1000,
            PingIntervalSeconds = 0,
            LocalLogsOnly = false,
            Hotkey = new HotkeyConfig { Modifiers = 0, Key = 82 }, // invalid: no modifier
            Overlay = new OverlayConfig { FontSize = 2, TextOpacity = 5.0, BackgroundOpacity = -1.0, FontFamily = "" }
        };

        var migrated = SettingsManager.ValidateAndMigrate(raw);

        Assert.Equal(1, migrated.ConfigVersion);
        Assert.Equal(500, migrated.PollingIntervalMs);
        Assert.Equal(30000, migrated.ProviderTimeoutMs);
        Assert.Equal(10, migrated.AlertCooldownSeconds);
        Assert.Equal(365, migrated.LogRetentionDays);
        Assert.Equal(2, migrated.PingIntervalSeconds);
        Assert.True(migrated.LocalLogsOnly);
        Assert.Equal(VirtualKeyHelper.ModAlt, migrated.Hotkey.Modifiers);
        Assert.Equal(82, migrated.Hotkey.Key);
        Assert.Equal(8, migrated.Overlay.FontSize);
        Assert.Equal(1.0, migrated.Overlay.TextOpacity);
        Assert.Equal(0.0, migrated.Overlay.BackgroundOpacity);
        Assert.Equal("Segoe UI", migrated.Overlay.FontFamily);
    }

    [Fact]
    public void VirtualKeyHelper_FormatsStandardCombinations()
    {
        Assert.Equal("Alt + R", VirtualKeyHelper.FormatHotkey(VirtualKeyHelper.ModAlt, 82));
        Assert.Equal("Ctrl + Shift + F10", VirtualKeyHelper.FormatHotkey(VirtualKeyHelper.ModControl | VirtualKeyHelper.ModShift, 0x79));
    }

    [Fact]
    public void VirtualKeyHelper_RejectsModifierOnlyOrNoModifierHotkeys()
    {
        Assert.False(VirtualKeyHelper.IsValidHotkey(0, 82));
        Assert.False(VirtualKeyHelper.IsValidHotkey(VirtualKeyHelper.ModAlt, 0x12)); // VK_MENU
        Assert.True(VirtualKeyHelper.IsValidHotkey(VirtualKeyHelper.ModAlt, 82));
    }
}
