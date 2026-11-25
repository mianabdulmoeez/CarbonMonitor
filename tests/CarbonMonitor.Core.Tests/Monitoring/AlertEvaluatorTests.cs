using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using Xunit;

namespace CarbonMonitor.Core.Tests.Monitoring;

public class AlertEvaluatorTests
{
    private static AppSettings CreateSettingsWithThreshold(string metricId, double? warn, double? crit, int cooldownSec = 60)
    {
        return new AppSettings
        {
            EnableWarningAlerts = true,
            AlertCooldownSeconds = cooldownSec,
            Metrics = new List<MetricConfig>
            {
                new()
                {
                    MetricId = metricId,
                    Enabled = true,
                    WarningThreshold = warn,
                    CriticalThreshold = crit
                }
            }
        };
    }

    private static SensorReading Reading(string metricId, double value, SensorAvailability avail = SensorAvailability.Available) => new()
    {
        MetricId = metricId,
        DisplayName = "CPU Temp",
        Value = value,
        Unit = SensorUnit.Celsius,
        Category = SensorCategory.Cpu,
        Availability = avail
    };

    [Fact]
    public void Evaluate_BelowWarning_ReturnsNoAlerts()
    {
        var evaluator = new AlertEvaluator();
        var settings = CreateSettingsWithThreshold("cpu.temp", 80, 95);

        var alerts = evaluator.Evaluate(new[] { Reading("cpu.temp", 65) }, settings);
        Assert.Empty(alerts);
    }

    [Fact]
    public void Evaluate_ReachesWarning_EmitsWarningAlert()
    {
        var evaluator = new AlertEvaluator();
        var settings = CreateSettingsWithThreshold("cpu.temp", 80, 95);

        var alerts = evaluator.Evaluate(new[] { Reading("cpu.temp", 85) }, settings);
        Assert.Single(alerts);
        Assert.Equal(AlertSeverity.Warning, alerts[0].Severity);
    }

    [Fact]
    public void Evaluate_SameConditionWithinCooldown_IsRateLimited()
    {
        var evaluator = new AlertEvaluator();
        var settings = CreateSettingsWithThreshold("cpu.temp", 80, 95, cooldownSec: 60);
        var t0 = DateTimeOffset.UtcNow;

        var first = evaluator.Evaluate(new[] { Reading("cpu.temp", 85) }, settings, t0);
        var second = evaluator.Evaluate(new[] { Reading("cpu.temp", 86) }, settings, t0.AddSeconds(15));
        var third = evaluator.Evaluate(new[] { Reading("cpu.temp", 86) }, settings, t0.AddSeconds(61));

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Single(third);
    }

    [Fact]
    public void Evaluate_EscalatesToCritical_EmitsImmediately()
    {
        var evaluator = new AlertEvaluator();
        var settings = CreateSettingsWithThreshold("cpu.temp", 80, 95, cooldownSec: 60);
        var t0 = DateTimeOffset.UtcNow;

        var warn = evaluator.Evaluate(new[] { Reading("cpu.temp", 85) }, settings, t0);
        var crit = evaluator.Evaluate(new[] { Reading("cpu.temp", 96) }, settings, t0.AddSeconds(5));

        Assert.Single(warn);
        Assert.Single(crit);
        Assert.Equal(AlertSeverity.Critical, crit[0].Severity);
    }

    [Fact]
    public void Evaluate_DisabledAlerts_ReturnsEmpty()
    {
        var evaluator = new AlertEvaluator();
        var settings = CreateSettingsWithThreshold("cpu.temp", 80, 95);
        settings.EnableWarningAlerts = false;

        var alerts = evaluator.Evaluate(new[] { Reading("cpu.temp", 99) }, settings);
        Assert.Empty(alerts);
    }
}
