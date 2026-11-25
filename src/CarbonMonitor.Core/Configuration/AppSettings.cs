namespace CarbonMonitor.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Monitoring mode options.
/// </summary>
public enum MonitoringMode
{
    Always,
    GameOnly,
    OverlayOnly
}

/// <summary>
/// Configuration for a keyboard hotkey.
/// </summary>
public class HotkeyConfig
{
    /// <summary>Key modifiers (1=Alt, 2=Ctrl, 4=Shift, 8=Win).</summary>
    public int Modifiers { get; set; } = VirtualKeyHelper.ModAlt;

    /// <summary>Virtual key code (default 82 = 'R').</summary>
    public int Key { get; set; } = 82;

    /// <summary>Display string for the hotkey.</summary>
    [JsonIgnore]
    public string DisplayString => VirtualKeyHelper.FormatHotkey(Modifiers, Key);
}

/// <summary>
/// Configuration for overlay appearance and placement.
/// </summary>
public class OverlayConfig
{
    public string Position { get; set; } = "TopRight";
    public int FontSize { get; set; } = 12;
    public string FontFamily { get; set; } = "Segoe UI";
    public double TextOpacity { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.7;
    public bool ShowBackground { get; set; } = true;
    public bool CompactSpacing { get; set; } = true;
    public int? AutoHideSeconds { get; set; }
    public int DisplayIndex { get; set; } = 0;
    public int OffsetX { get; set; } = 10;
    public int OffsetY { get; set; } = 10;
    public bool WarningHighlight { get; set; } = true;
    public bool HighContrast { get; set; } = false;
    public bool InteractiveMode { get; set; } = false;
    public int? CustomX { get; set; }
    public int? CustomY { get; set; }
}

/// <summary>
/// Configuration for a single metric.
/// </summary>
public class MetricConfig
{
    public string MetricId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public double? WarningThreshold { get; set; }
    public double? CriticalThreshold { get; set; }
    public string? CustomLabel { get; set; }
}

/// <summary>
/// Game process configuration.
/// </summary>
public class GameProcessConfig
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Root application settings.
/// </summary>
public class AppSettings
{
    public int ConfigVersion { get; set; } = 1;

    // General
    public bool LaunchAtStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool StartOverlayVisible { get; set; } = false;

    // Monitoring & power behavior
    public MonitoringMode MonitoringMode { get; set; } = MonitoringMode.Always;
    public int PollingIntervalMs { get; set; } = 2000;
    public int ProviderTimeoutMs { get; set; } = 5000;
    public bool PauseOnBattery { get; set; } = false;
    public bool ReducePollingOnBattery { get; set; } = true;
    public int BatteryPollingIntervalMs { get; set; } = 5000;

    // Hotkey
    public HotkeyConfig Hotkey { get; set; } = new();

    // Overlay
    public OverlayConfig Overlay { get; set; } = new();

    // Metrics
    public List<MetricConfig> Metrics { get; set; } = new();
    public bool ShowUnavailableMetrics { get; set; } = false;

    // Game processes & gaming telemetry
    public List<GameProcessConfig> GameProcesses { get; set; } = new()
    {
        new GameProcessConfig { ProcessName = "VALORANT-Win64-Shipping", DisplayName = "VALORANT" }
    };
    public bool EnableFpsCapture { get; set; } = false;
    public string PingTarget { get; set; } = "8.8.8.8";
    public int PingIntervalSeconds { get; set; } = 5;

    // Alerts
    public bool EnableWarningAlerts { get; set; } = true;
    public int AlertCooldownSeconds { get; set; } = 60;

    // Dashboard appearance
    public string DashboardTheme { get; set; } = "DarkGaming";
    public bool ShowDashboardGraphs { get; set; } = true;
    public bool ShowPerCoreCpu { get; set; } = true;

    // Privacy & Diagnostics
    public bool LocalLogsOnly { get; set; } = true;
    public int LogRetentionDays { get; set; } = 7;
    public bool VerboseLogging { get; set; } = false;

    /// <summary>Creates a deep copy of the settings.</summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    /// <summary>Populates default metrics if none are configured yet.</summary>
    public void EnsureDefaultMetrics()
    {
        if (Metrics.Count == 0)
            Metrics = CreateDefaultMetrics();
    }

    /// <summary>Default ordered overlay metrics with conservative laptop/desktop thresholds.</summary>
    public static List<MetricConfig> CreateDefaultMetrics()
    {
        int order = 0;
        MetricConfig Item(string id, bool enabled = true, double? warn = null, double? crit = null) => new()
        {
            MetricId = id,
            Enabled = enabled,
            DisplayOrder = order++,
            WarningThreshold = warn,
            CriticalThreshold = crit
        };

        return new List<MetricConfig>
        {
            Item("cpu.usage.total", true, 85, 95),
            Item("lhm.cpu.temperature.package", true, 82, 95),
            Item("cpu.temperature.thermalzone", true, 82, 95),
            Item("cpu.clock.current", true),
            Item("lhm.cpu.power", false),
            Item("gpu.usage", true, 90, 98),
            Item("gpu.temperature", true, 80, 92),
            Item("gpu.clock", false),
            Item("gpu.memory.dedicated", true),
            Item("gpu.memory.shared", false),
            Item("memory.usage.percent", true, 85, 95),
            Item("memory.used", true),
            Item("disk.usage.percent", false, 90, 97),
            Item("disk.read.speed", false),
            Item("disk.write.speed", false),
            Item("storage.temperature", false, 65, 75),
            Item("cooling.fan.rpm", false),
            Item("game.fps", true),
            Item("game.frametime", true),
            Item("game.fps.1percent_low", false),
            Item("network.ping.latency", true, 120, 250),
            Item("network.ping.packetloss", false, 2, 5),
            Item("network.download.speed", false),
            Item("network.upload.speed", false)
        };
    }
}
