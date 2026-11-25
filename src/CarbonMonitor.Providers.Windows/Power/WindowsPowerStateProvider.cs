using System.Runtime.Versioning;
using CarbonMonitor.Core.Scheduling;

namespace CarbonMonitor.Providers.Windows.Power;

/// <summary>
/// Queries Windows AC/Battery power state using SystemInformation.PowerStatus.
/// Used by AdaptivePollingPolicy to avoid unnecessary laptop battery drain.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsPowerStateProvider
{
    public static PowerSourceKind GetCurrentPowerSource()
    {
        try
        {
            return SystemInformation.PowerStatus.PowerLineStatus switch
            {
                PowerLineStatus.Online => PowerSourceKind.Ac,
                PowerLineStatus.Offline => PowerSourceKind.Battery,
                _ => PowerSourceKind.Unknown
            };
        }
        catch
        {
            return PowerSourceKind.Unknown;
        }
    }

    public static int? GetBatteryPercent()
    {
        try
        {
            float life = SystemInformation.PowerStatus.BatteryLifePercent;
            if (life is >= 0f and <= 1.0f)
                return (int)Math.Round(life * 100f);
            return null;
        }
        catch
        {
            return null;
        }
    }
}
