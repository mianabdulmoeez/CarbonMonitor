namespace CarbonMonitor.Core.Tests.Configuration;

using System.Text.Json;
using CarbonMonitor.Core.Configuration;
using Xunit;

public class AppSettingsTests
{
    // ── Default values ──

    [Fact]
    public void Default_ConfigVersion_IsOne()
    {
        var settings = new AppSettings();
        Assert.Equal(1, settings.ConfigVersion);
    }

    [Fact]
    public void Default_MonitoringMode_IsAlways()
    {
        var settings = new AppSettings();
        Assert.Equal(MonitoringMode.Always, settings.MonitoringMode);
    }

    [Fact]
    public void Default_Hotkey_IsAltR()
    {
        var settings = new AppSettings();
        Assert.Equal(1, settings.Hotkey.Modifiers); // Alt
        Assert.Equal(82, settings.Hotkey.Key);       // R
    }

    [Fact]
    public void Default_LaunchAtStartup_IsFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.LaunchAtStartup);
    }

    [Fact]
    public void Default_StartMinimized_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.StartMinimized);
    }

    [Fact]
    public void Default_StartOverlayVisible_IsFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.StartOverlayVisible);
    }

    [Fact]
    public void Default_PollingInterval_Is2000Ms()
    {
        var settings = new AppSettings();
        Assert.Equal(2000, settings.PollingIntervalMs);
    }

    [Fact]
    public void Default_ProviderTimeout_Is5000Ms()
    {
        var settings = new AppSettings();
        Assert.Equal(5000, settings.ProviderTimeoutMs);
    }

    // ── Game process defaults ──

    [Fact]
    public void Default_GameProcesses_IncludesValorant()
    {
        var settings = new AppSettings();
        Assert.Contains(settings.GameProcesses,
            g => g.DisplayName == "VALORANT");
    }

    [Fact]
    public void Default_GameProcesses_ValorantProcessName_IsCorrect()
    {
        var settings = new AppSettings();
        var valorant = settings.GameProcesses.First(g => g.DisplayName == "VALORANT");
        Assert.Equal("VALORANT-Win64-Shipping", valorant.ProcessName);
    }

    // ── Overlay defaults ──

    [Fact]
    public void Default_Overlay_Position_IsTopRight()
    {
        var settings = new AppSettings();
        Assert.Equal("TopRight", settings.Overlay.Position);
    }

    [Fact]
    public void Default_Overlay_FontSize_Is12()
    {
        var settings = new AppSettings();
        Assert.Equal(12, settings.Overlay.FontSize);
    }

    [Fact]
    public void Default_Overlay_ShowBackground_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.Overlay.ShowBackground);
    }

    // ── Alert defaults ──

    [Fact]
    public void Default_EnableWarningAlerts_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.EnableWarningAlerts);
    }

    [Fact]
    public void Default_AlertCooldown_Is60Seconds()
    {
        var settings = new AppSettings();
        Assert.Equal(60, settings.AlertCooldownSeconds);
    }

    // ── Privacy defaults ──

    [Fact]
    public void Default_LocalLogsOnly_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.LocalLogsOnly);
    }

    [Fact]
    public void Default_LogRetention_Is7Days()
    {
        var settings = new AppSettings();
        Assert.Equal(7, settings.LogRetentionDays);
    }

    // ── MetricConfig defaults ──

    [Fact]
    public void MetricConfig_Default_IsEnabled()
    {
        var metric = new MetricConfig();
        Assert.True(metric.Enabled);
    }

    [Fact]
    public void MetricConfig_Default_DisplayOrder_IsZero()
    {
        var metric = new MetricConfig();
        Assert.Equal(0, metric.DisplayOrder);
    }

    [Fact]
    public void MetricConfig_Default_NoThresholds()
    {
        var metric = new MetricConfig();
        Assert.Null(metric.WarningThreshold);
        Assert.Null(metric.CriticalThreshold);
    }

    [Fact]
    public void MetricConfig_Default_NoCustomLabel()
    {
        var metric = new MetricConfig();
        Assert.Null(metric.CustomLabel);
    }

    // ── JSON serialization roundtrip ──

    [Fact]
    public void JsonRoundtrip_PreservesAllFields()
    {
        var original = new AppSettings
        {
            ConfigVersion = 1,
            LaunchAtStartup = true,
            StartMinimized = false,
            StartOverlayVisible = true,
            MonitoringMode = MonitoringMode.GameOnly,
            PollingIntervalMs = 3000,
            ProviderTimeoutMs = 10000,
            Hotkey = new HotkeyConfig { Modifiers = 6, Key = 121 }, // Ctrl+Shift+F10
            Overlay = new OverlayConfig
            {
                Position = "BottomLeft",
                FontSize = 14,
                FontFamily = "Consolas",
                TextOpacity = 0.9,
                BackgroundOpacity = 0.5,
                ShowBackground = false,
                CompactSpacing = false,
                AutoHideSeconds = 30,
                DisplayIndex = 1,
                OffsetX = 20,
                OffsetY = 20
            },
            Metrics = new List<MetricConfig>
            {
                new() { MetricId = "cpu.usage", Enabled = true, DisplayOrder = 0, WarningThreshold = 90 },
                new() { MetricId = "cpu.temp", Enabled = false, DisplayOrder = 1, CriticalThreshold = 95 }
            },
            GameProcesses = new List<GameProcessConfig>
            {
                new() { ProcessName = "VALORANT-Win64-Shipping", DisplayName = "VALORANT" },
                new() { ProcessName = "cs2", DisplayName = "CS2" }
            },
            EnableWarningAlerts = false,
            AlertCooldownSeconds = 120,
            LocalLogsOnly = true,
            LogRetentionDays = 14
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ConfigVersion, deserialized.ConfigVersion);
        Assert.Equal(original.LaunchAtStartup, deserialized.LaunchAtStartup);
        Assert.Equal(original.StartMinimized, deserialized.StartMinimized);
        Assert.Equal(original.StartOverlayVisible, deserialized.StartOverlayVisible);
        Assert.Equal(original.MonitoringMode, deserialized.MonitoringMode);
        Assert.Equal(original.PollingIntervalMs, deserialized.PollingIntervalMs);
        Assert.Equal(original.ProviderTimeoutMs, deserialized.ProviderTimeoutMs);
        Assert.Equal(original.Hotkey.Modifiers, deserialized.Hotkey.Modifiers);
        Assert.Equal(original.Hotkey.Key, deserialized.Hotkey.Key);
        Assert.Equal(original.Overlay.Position, deserialized.Overlay.Position);
        Assert.Equal(original.Overlay.FontSize, deserialized.Overlay.FontSize);
        Assert.Equal(original.Overlay.FontFamily, deserialized.Overlay.FontFamily);
        Assert.Equal(original.Overlay.TextOpacity, deserialized.Overlay.TextOpacity);
        Assert.Equal(original.Overlay.BackgroundOpacity, deserialized.Overlay.BackgroundOpacity);
        Assert.Equal(original.Overlay.ShowBackground, deserialized.Overlay.ShowBackground);
        Assert.Equal(original.Overlay.CompactSpacing, deserialized.Overlay.CompactSpacing);
        Assert.Equal(original.Overlay.AutoHideSeconds, deserialized.Overlay.AutoHideSeconds);
        Assert.Equal(original.Overlay.DisplayIndex, deserialized.Overlay.DisplayIndex);
        Assert.Equal(original.Overlay.OffsetX, deserialized.Overlay.OffsetX);
        Assert.Equal(original.Overlay.OffsetY, deserialized.Overlay.OffsetY);
        Assert.Equal(2, deserialized.Metrics.Count);
        Assert.Equal("cpu.usage", deserialized.Metrics[0].MetricId);
        Assert.Equal(90, deserialized.Metrics[0].WarningThreshold);
        Assert.False(deserialized.Metrics[1].Enabled);
        Assert.Equal(2, deserialized.GameProcesses.Count);
        Assert.Equal(original.EnableWarningAlerts, deserialized.EnableWarningAlerts);
        Assert.Equal(original.AlertCooldownSeconds, deserialized.AlertCooldownSeconds);
        Assert.Equal(original.LogRetentionDays, deserialized.LogRetentionDays);
    }

    [Fact]
    public void Deserialization_PartialJson_FillsDefaults()
    {
        // Only specifying a subset of properties
        var partialJson = """{"ConfigVersion":1,"LaunchAtStartup":true}""";
        var settings = JsonSerializer.Deserialize<AppSettings>(partialJson);

        Assert.NotNull(settings);
        Assert.Equal(1, settings.ConfigVersion);
        Assert.True(settings.LaunchAtStartup);
        // All other properties should retain their defaults
        Assert.True(settings.StartMinimized);
        Assert.False(settings.StartOverlayVisible);
        Assert.Equal(MonitoringMode.Always, settings.MonitoringMode);
        Assert.Equal(2000, settings.PollingIntervalMs);
        Assert.NotNull(settings.Hotkey);
        Assert.NotNull(settings.Overlay);
    }

    [Fact]
    public void Deserialization_EmptyJson_ReturnsDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.NotNull(settings);
        Assert.Equal(1, settings.ConfigVersion); // Retains class default
        Assert.Equal(MonitoringMode.Always, settings.MonitoringMode);
    }

    // ── Clone ──

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new AppSettings
        {
            PollingIntervalMs = 5000,
            Hotkey = new HotkeyConfig { Modifiers = 2, Key = 65 }
        };

        var clone = original.Clone();

        // Modify clone; original should not change
        clone.PollingIntervalMs = 1000;
        clone.Hotkey.Key = 66;

        Assert.Equal(5000, original.PollingIntervalMs);
        Assert.Equal(65, original.Hotkey.Key);
    }

    // ── HotkeyConfig ──

    [Fact]
    public void HotkeyConfig_Default_Values()
    {
        var hotkey = new HotkeyConfig();
        Assert.Equal(1, hotkey.Modifiers);
        Assert.Equal(82, hotkey.Key);
    }

    // ── OverlayConfig defaults ──

    [Fact]
    public void OverlayConfig_Default_Values()
    {
        var overlay = new OverlayConfig();
        Assert.Equal("TopRight", overlay.Position);
        Assert.Equal(12, overlay.FontSize);
        Assert.Equal("Segoe UI", overlay.FontFamily);
        Assert.Equal(1.0, overlay.TextOpacity);
        Assert.Equal(0.7, overlay.BackgroundOpacity);
        Assert.True(overlay.ShowBackground);
        Assert.True(overlay.CompactSpacing);
        Assert.Null(overlay.AutoHideSeconds);
        Assert.Equal(0, overlay.DisplayIndex);
        Assert.Equal(10, overlay.OffsetX);
        Assert.Equal(10, overlay.OffsetY);
    }

    // ── GameProcessConfig ──

    [Fact]
    public void GameProcessConfig_Default_HasEmptyStrings()
    {
        var config = new GameProcessConfig();
        Assert.Equal(string.Empty, config.ProcessName);
        Assert.Equal(string.Empty, config.DisplayName);
    }

    // ── Sensible defaults check ──

    [Fact]
    public void Default_Settings_HaveSensibleValues()
    {
        var settings = new AppSettings();

        // Polling should be reasonable (1-10 seconds)
        Assert.InRange(settings.PollingIntervalMs, 500, 10000);

        // Provider timeout should be reasonable
        Assert.InRange(settings.ProviderTimeoutMs, 1000, 30000);

        // Alert cooldown should prevent spam
        Assert.True(settings.AlertCooldownSeconds >= 10);

        // Log retention should be bounded
        Assert.InRange(settings.LogRetentionDays, 1, 365);

        // Overlay opacity should be valid
        Assert.InRange(settings.Overlay.TextOpacity, 0.0, 1.0);
        Assert.InRange(settings.Overlay.BackgroundOpacity, 0.0, 1.0);

        // Font size should be readable
        Assert.InRange(settings.Overlay.FontSize, 6, 72);
    }
}
