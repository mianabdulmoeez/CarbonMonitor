using CarbonMonitor.Core.Configuration;

namespace CarbonMonitor.Core.Scheduling;

public enum PowerSourceKind
{
    Unknown,
    Ac,
    Battery
}

/// <summary>
/// Computes whether monitoring should actively poll and at what cadence
/// based on monitoring mode, overlay visibility, game activity, and AC/battery power state.
/// </summary>
public static class AdaptivePollingPolicy
{
    public static bool ShouldPoll(
        AppSettings settings,
        bool isOverlayVisible,
        bool isDashboardOpen,
        bool isSelectedGameRunning,
        PowerSourceKind powerSource)
    {
        if (settings.PauseOnBattery && powerSource == PowerSourceKind.Battery && !isDashboardOpen)
            return false;

        return settings.MonitoringMode switch
        {
            MonitoringMode.Always => true,
            MonitoringMode.GameOnly => isSelectedGameRunning || isDashboardOpen,
            MonitoringMode.OverlayOnly => isOverlayVisible || isDashboardOpen,
            _ => true
        };
    }

    public static int GetEffectivePollingIntervalMs(AppSettings settings, PowerSourceKind powerSource)
    {
        if (settings.ReducePollingOnBattery && powerSource == PowerSourceKind.Battery)
            return Math.Max(settings.PollingIntervalMs, settings.BatteryPollingIntervalMs);

        return settings.PollingIntervalMs;
    }
}
