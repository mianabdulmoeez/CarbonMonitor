namespace CarbonMonitor.Core.Models;

/// <summary>
/// Categories of hardware and telemetry sensors.
/// </summary>
public enum SensorCategory
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Gaming,
    Cooling
}

/// <summary>
/// Unit of measurement for a sensor reading.
/// </summary>
public enum SensorUnit
{
    Celsius,
    Fahrenheit,
    Percent,
    MHz,
    GHz,
    MB,
    GB,
    MBPerSecond,
    Rpm,
    Fps,
    Milliseconds,
    Watts,
    Volts,
    Count,
    None
}

/// <summary>
/// Availability status of a sensor.
/// </summary>
public enum SensorAvailability
{
    Available,
    Unsupported,
    PermissionRequired,
    TemporarilyUnavailable,
    Error,
    NotDetected
}

/// <summary>
/// Source classification of a sensor reading.
/// </summary>
public enum SensorSourceType
{
    Measured,
    OsCounter,
    Estimated,
    Calculated
}

/// <summary>
/// Represents a single sensor reading with hardware identity, source, timestamp, and availability metadata.
/// </summary>
public class SensorReading
{
    /// <summary>Stable metric identifier (e.g., "cpu.temperature.package").</summary>
    public string MetricId { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Current value, or null if unavailable.</summary>
    public double? Value { get; set; }

    /// <summary>Optional string text for non-numeric status metrics (e.g. drive health).</summary>
    public string? TextValue { get; set; }

    /// <summary>Unit of measurement.</summary>
    public SensorUnit Unit { get; set; } = SensorUnit.None;

    /// <summary>Sensor category.</summary>
    public SensorCategory Category { get; set; }

    /// <summary>Hardware adapter identifier.</summary>
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>Friendly hardware name.</summary>
    public string HardwareName { get; set; } = string.Empty;

    /// <summary>Provider/source name.</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>When the reading was taken (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last timestamp when this metric produced an Available value.</summary>
    public DateTimeOffset? LastSuccessfulUpdate { get; set; }

    /// <summary>Availability status.</summary>
    public SensorAvailability Availability { get; set; } = SensorAvailability.Available;

    /// <summary>Reason for unavailability, if any.</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>Explicit permission requirement when restricted (e.g., Administrator).</summary>
    public string? PermissionRequirement { get; set; }

    /// <summary>Source classification of the reading.</summary>
    public SensorSourceType SourceType { get; set; } = SensorSourceType.Measured;

    /// <summary>Optional confidence/quality note.</summary>
    public string? QualityNote { get; set; }

    /// <summary>Age of this sample relative to UTC now.</summary>
    public TimeSpan SampleAge => DateTimeOffset.UtcNow - Timestamp;

    /// <summary>Returns true if the sample timestamp is older than the specified threshold.</summary>
    public bool IsStale(TimeSpan maxAge) => SampleAge > maxAge;

    /// <summary>Formatted display value with unit.</summary>
    public string DisplayValue
    {
        get
        {
            if (Availability != SensorAvailability.Available)
                return "N/A";

            if (!string.IsNullOrWhiteSpace(TextValue) && Value is null)
                return TextValue;

            if (Value is null)
                return "N/A";

            var unitSuffix = Unit switch
            {
                SensorUnit.Celsius => " °C",
                SensorUnit.Fahrenheit => " °F",
                SensorUnit.Percent => " %",
                SensorUnit.MHz => " MHz",
                SensorUnit.GHz => " GHz",
                SensorUnit.MB => " MB",
                SensorUnit.GB => " GB",
                SensorUnit.MBPerSecond => " MB/s",
                SensorUnit.Rpm => " RPM",
                SensorUnit.Fps => " FPS",
                SensorUnit.Milliseconds => " ms",
                SensorUnit.Watts => " W",
                SensorUnit.Volts => " V",
                SensorUnit.Count => "",
                SensorUnit.None => "",
                _ => ""
            };

            if (Unit is SensorUnit.MHz or SensorUnit.Rpm or SensorUnit.Fps or SensorUnit.Count)
                return $"{Value.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}{unitSuffix}";

            if (Unit is SensorUnit.Celsius or SensorUnit.Fahrenheit or SensorUnit.Percent
                or SensorUnit.GHz or SensorUnit.Milliseconds or SensorUnit.Watts or SensorUnit.Volts
                or SensorUnit.MB or SensorUnit.GB or SensorUnit.MBPerSecond)
                return $"{Value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}{unitSuffix}";

            return $"{Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}{unitSuffix}";
        }
    }

    /// <summary>Factory method for an unavailable sensor reading.</summary>
    public static SensorReading Unavailable(
        string metricId,
        string displayName,
        SensorCategory category,
        string reason,
        string providerName = "")
    {
        return Unavailable(
            metricId,
            displayName,
            category,
            hardwareId: string.Empty,
            hardwareName: string.Empty,
            providerName: providerName,
            availability: SensorAvailability.Unsupported,
            reason: reason);
    }

    /// <summary>Detailed factory method for an unavailable sensor reading with hardware identity and explicit availability state.</summary>
    public static SensorReading Unavailable(
        string metricId,
        string displayName,
        SensorCategory category,
        string hardwareId,
        string hardwareName,
        string providerName,
        SensorAvailability availability,
        string reason,
        string? qualityNote = null,
        string? permissionRequirement = null)
    {
        return new SensorReading
        {
            MetricId = metricId,
            DisplayName = displayName,
            Value = null,
            Unit = SensorUnit.None,
            Category = category,
            HardwareId = hardwareId,
            HardwareName = hardwareName,
            ProviderName = providerName,
            Timestamp = DateTimeOffset.UtcNow,
            Availability = availability,
            UnavailableReason = reason,
            PermissionRequirement = permissionRequirement ?? (availability == SensorAvailability.PermissionRequired ? "Administrator" : null),
            SourceType = SensorSourceType.Measured,
            QualityNote = qualityNote
        };
    }
}
