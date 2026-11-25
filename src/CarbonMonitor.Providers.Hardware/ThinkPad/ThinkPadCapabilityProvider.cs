using System.Management;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Hardware.ThinkPad;

/// <summary>
/// Read-only platform capability detector for Lenovo ThinkPad systems (e.g., ThinkPad X1 Carbon 6th Gen).
/// Strictly read-only: performs NO Embedded Controller (EC) register writes and NO fan control.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ThinkPadCapabilityProvider : IHardwareProvider
{
    private readonly List<HardwareInfo> _hardware = new();
    private string _systemModel = "Unknown System";
    private bool _isThinkPad;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;

    public string Name => "Hardware.ThinkPad.Capability";
    public string Description => "Lenovo ThinkPad read-only platform capability detector (no EC writes, no fan control)";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "cooling.thinkpad.acpi_thermal"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "WMI requires Windows.";
            return Task.FromResult(false);
        }

        try
        {
            _hardware.Clear();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Vendor, Name, Version FROM Win32_ComputerSystemProduct");

            foreach (ManagementObject obj in searcher.Get())
            {
                var vendor = obj["Vendor"]?.ToString()?.Trim() ?? "";
                var name = obj["Name"]?.ToString()?.Trim() ?? "";
                var version = obj["Version"]?.ToString()?.Trim() ?? "";
                _systemModel = string.IsNullOrWhiteSpace(version) ? $"{vendor} {name}".Trim() : $"{vendor} {version} ({name})".Trim();
                _isThinkPad = _systemModel.Contains("ThinkPad", StringComparison.OrdinalIgnoreCase) ||
                              vendor.Contains("LENOVO", StringComparison.OrdinalIgnoreCase);

                if (_isThinkPad)
                {
                    _hardware.Add(new HardwareInfo(
                        "platform.thinkpad",
                        _systemModel,
                        "Lenovo",
                        null,
                        SensorCategory.Cooling,
                        "Read-only safety policy active: EC register writes and manual fan curves disabled."));
                }
                break;
            }

            IsAvailable = _isThinkPad;
            Health = ProviderHealth.Healthy;
            HealthReason = _isThinkPad
                ? $"Detected {_systemModel}. Read-only WMI thermal check active (no EC writes)."
                : $"Non-ThinkPad system ({_systemModel}); ThinkPad capability probe inactive.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = $"Platform detection failed: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        _lastChecked = DateTimeOffset.UtcNow;
        if (!IsAvailable)
            return Task.FromResult<IReadOnlyList<SensorReading>>(Array.Empty<SensorReading>());

        var readings = new List<SensorReading>();
        try
        {
            // Safe, documented ACPI thermal zone check via root\WMI (no EC writes)
            using var tzSearcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT CurrentTemperature, InstanceName FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject tz in tzSearcher.Get())
            {
                if (tz["CurrentTemperature"] is uint tenthsKelvin && tenthsKelvin > 2000)
                {
                    double celsius = (tenthsKelvin / 10.0) - 273.15;
                    readings.Add(new SensorReading
                    {
                        MetricId = "cooling.thinkpad.acpi_thermal",
                        DisplayName = "ACPI Thermal Zone",
                        Value = Math.Round(celsius, 1),
                        Unit = SensorUnit.Celsius,
                        Category = SensorCategory.Cooling,
                        HardwareId = "platform.thinkpad",
                        HardwareName = _systemModel,
                        ProviderName = Name,
                        Timestamp = _lastChecked,
                        Availability = SensorAvailability.Available,
                        SourceType = SensorSourceType.Measured,
                        QualityNote = "Windows ACPI thermal zone (tenths of Kelvin); not a GPU sensor."
                    });
                    break;
                }
            }
        }
        catch (ManagementException)
        {
            readings.Add(SensorReading.Unavailable(
                "cooling.thinkpad.acpi_thermal",
                "ACPI Thermal Zone",
                SensorCategory.Cooling,
                "platform.thinkpad",
                _systemModel,
                Name,
                SensorAvailability.PermissionRequired,
                "MSAcpi_ThermalZoneTemperature requires elevated WMI permission or is not exposed by firmware. EC register writes are intentionally disabled."));
        }
        catch (Exception ex)
        {
            readings.Add(SensorReading.Unavailable(
                "cooling.thinkpad.acpi_thermal",
                "ACPI Thermal Zone",
                SensorCategory.Cooling,
                "platform.thinkpad",
                _systemModel,
                Name,
                SensorAvailability.Unsupported,
                ex.Message));
        }

        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    public IReadOnlyList<HardwareInfo> GetDetectedHardware() => _hardware.AsReadOnly();

    public ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        _lastChecked == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _lastChecked,
        IsAvailable ? 1 : 0,
        IsAvailable);

    public void Dispose() { }
}
