using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Scheduling;
using Xunit;

namespace CarbonMonitor.Core.Tests.Scheduling;

public class AdaptivePollingPolicyTests
{
    [Fact]
    public void ShouldPoll_AlwaysMode_OnAc_ReturnsTrue()
    {
        var settings = new AppSettings { MonitoringMode = MonitoringMode.Always };
        Assert.True(AdaptivePollingPolicy.ShouldPoll(settings, false, false, false, PowerSourceKind.Ac));
    }

    [Fact]
    public void ShouldPoll_GameOnlyMode_OnlyTrueWhenGameOrDashboardActive()
    {
        var settings = new AppSettings { MonitoringMode = MonitoringMode.GameOnly };

        Assert.False(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: true, isDashboardOpen: false, isSelectedGameRunning: false, PowerSourceKind.Ac));
        Assert.True(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: false, isDashboardOpen: false, isSelectedGameRunning: true, PowerSourceKind.Ac));
        Assert.True(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: false, isDashboardOpen: true, isSelectedGameRunning: false, PowerSourceKind.Ac));
    }

    [Fact]
    public void ShouldPoll_OverlayOnlyMode_OnlyTrueWhenOverlayOrDashboardVisible()
    {
        var settings = new AppSettings { MonitoringMode = MonitoringMode.OverlayOnly };

        Assert.False(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: false, isDashboardOpen: false, isSelectedGameRunning: true, PowerSourceKind.Ac));
        Assert.True(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: true, isDashboardOpen: false, isSelectedGameRunning: false, PowerSourceKind.Ac));
    }

    [Fact]
    public void ShouldPoll_PauseOnBattery_PausesBackgroundWhenOnBattery()
    {
        var settings = new AppSettings { MonitoringMode = MonitoringMode.Always, PauseOnBattery = true };

        Assert.False(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: true, isDashboardOpen: false, isSelectedGameRunning: false, PowerSourceKind.Battery));
        Assert.True(AdaptivePollingPolicy.ShouldPoll(settings, isOverlayVisible: false, isDashboardOpen: true, isSelectedGameRunning: false, PowerSourceKind.Battery));
    }

    [Fact]
    public void GetEffectivePollingInterval_ReducesFrequencyOnBattery()
    {
        var settings = new AppSettings
        {
            PollingIntervalMs = 2000,
            BatteryPollingIntervalMs = 5000,
            ReducePollingOnBattery = true
        };

        Assert.Equal(2000, AdaptivePollingPolicy.GetEffectivePollingIntervalMs(settings, PowerSourceKind.Ac));
        Assert.Equal(5000, AdaptivePollingPolicy.GetEffectivePollingIntervalMs(settings, PowerSourceKind.Battery));
    }
}
