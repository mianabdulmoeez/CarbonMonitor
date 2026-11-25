using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Core.Monitoring;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record SensorAlert(
    string MetricId,
    string DisplayName,
    double Value,
    double Threshold,
    AlertSeverity Severity,
    string Message,
    DateTimeOffset Timestamp);

/// <summary>
/// Evaluates sensor readings against configured thresholds with rate limiting
/// so ongoing thermal or load conditions do not spam notifications.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly Dictionary<string, (AlertSeverity Severity, DateTimeOffset LastNotified)> _activeStates =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SensorAlert> Evaluate(
        IReadOnlyList<SensorReading> readings,
        AppSettings settings,
        DateTimeOffset? nowOverride = null)
    {
        if (!settings.EnableWarningAlerts || readings.Count == 0)
            return Array.Empty<SensorAlert>();

        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromSeconds(Math.Max(10, settings.AlertCooldownSeconds));
        var metricLookup = settings.Metrics.ToDictionary(m => m.MetricId, StringComparer.OrdinalIgnoreCase);
        var emitted = new List<SensorAlert>();

        foreach (var reading in readings)
        {
            if (reading.Availability != SensorAvailability.Available || reading.Value is not double val)
                continue;

            if (!metricLookup.TryGetValue(reading.MetricId, out var config))
                continue;

            AlertSeverity? currentSeverity = null;
            double threshold = 0;

            if (config.CriticalThreshold is double crit && val >= crit)
            {
                currentSeverity = AlertSeverity.Critical;
                threshold = crit;
            }
            else if (config.WarningThreshold is double warn && val >= warn)
            {
                currentSeverity = AlertSeverity.Warning;
                threshold = warn;
            }

            if (currentSeverity is null)
            {
                // Condition cleared
                _activeStates.Remove(reading.MetricId);
                continue;
            }

            bool shouldNotify = false;
            if (!_activeStates.TryGetValue(reading.MetricId, out var prev))
            {
                shouldNotify = true;
            }
            else if (currentSeverity.Value > prev.Severity)
            {
                // Escalated from Warning to Critical
                shouldNotify = true;
            }
            else if (now - prev.LastNotified >= cooldown)
            {
                shouldNotify = true;
            }

            if (shouldNotify)
            {
                _activeStates[reading.MetricId] = (currentSeverity.Value, now);
                emitted.Add(new SensorAlert(
                    reading.MetricId,
                    config.CustomLabel ?? reading.DisplayName,
                    val,
                    threshold,
                    currentSeverity.Value,
                    $"{currentSeverity.Value}: {reading.DisplayName} reached {reading.DisplayValue} (threshold {threshold:F1})",
                    now));
            }
        }

        return emitted.AsReadOnly();
    }

    public AlertSeverity GetSeverity(SensorReading reading, MetricConfig? config)
    {
        if (config is null || reading.Value is not double val || reading.Availability != SensorAvailability.Available)
            return AlertSeverity.Info;

        if (config.CriticalThreshold is double crit && val >= crit)
            return AlertSeverity.Critical;

        if (config.WarningThreshold is double warn && val >= warn)
            return AlertSeverity.Warning;

        return AlertSeverity.Info;
    }

    public void Reset() => _activeStates.Clear();
}
