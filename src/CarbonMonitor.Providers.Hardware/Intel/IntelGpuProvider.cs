using System.Management;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;

namespace CarbonMonitor.Providers.Hardware.Intel;

/// <summary>
/// Capability-reporting provider for Intel integrated GPUs (e.g., Intel UHD Graphics 620).
/// Uses canonical PCI/PnP IDs so it merges into the same physical GPU card as Windows.GpuEngine
/// and explicitly documents missing discrete GPU temperature/fan/clock sensors without relabeling CPU temp.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntelGpuProvider : IGpuTelemetryProvider
{
    private readonly List<HardwareInfo> _hardware = new();
    private string _gpuName = "Intel Integrated Graphics";
    private string? _pnpDeviceId;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;

    public string Name => "Hardware.Intel.IntegratedGpu";
    public string Description => "Intel integrated GPU capability and honest sensor availability reporting";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "intel.igpu.temperature",
        "intel.igpu.clock",
        "intel.igpu.fan"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }

    public bool SupportsTemperature => false;
    public bool SupportsClock => false;
    public bool SupportsMemoryUsage => true;
    public bool SupportsFanSpeed => false;
    public string GpuType => "Integrated";

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "Windows WMI required for Intel GPU discovery.";
            return Task.FromResult(false);
        }

        try
        {
            _hardware.Clear();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Xe", StringComparison.OrdinalIgnoreCase)))
                {
                    _gpuName = name;
                    var driver = obj["DriverVersion"]?.ToString()?.Trim();
                    _pnpDeviceId = obj["PNPDeviceID"]?.ToString()?.Trim();
                    var canonicalId = GpuReadingAggregator.ResolveCanonicalKey(_gpuName, _pnpDeviceId);

                    _hardware.Add(new HardwareInfo(
                        "intel.igpu0",
                        _gpuName,
                        "Intel",
                        driver,
                        SensorCategory.Gpu,
                        "Integrated Graphics (shared system memory; utilization tracked by Windows.GpuEngine)",
                        _pnpDeviceId,
                        canonicalId));
                    IsAvailable = true;
                    break;
                }
            }

            if (IsAvailable)
            {
                Health = ProviderHealth.Healthy;
                HealthReason = "Detected Intel integrated GPU. Utilization & shared memory come from Windows.GpuEngine; discrete GPU temp is not exposed on UHD 620.";
            }
            else
            {
                Health = ProviderHealth.Degraded;
                HealthReason = "No Intel integrated GPU adapter detected.";
            }
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"Intel iGPU discovery failed: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        _lastChecked = DateTimeOffset.UtcNow;
        if (!IsAvailable)
            return Task.FromResult<IReadOnlyList<SensorReading>>(Array.Empty<SensorReading>());

        var readings = new List<SensorReading>
        {
            SensorReading.Unavailable(
                "intel.igpu.temperature",
                $"{_gpuName} Temperature",
                SensorCategory.Gpu,
                "intel.igpu0",
                _gpuName,
                Name,
                SensorAvailability.Unsupported,
                "Intel UHD 620 hardware/driver does not expose a separate GPU temperature sensor; see CPU/ACPI thermal sensors separately.",
                "Never relabel CPU package temperature as GPU temperature."),
            SensorReading.Unavailable(
                "intel.igpu.clock",
                $"{_gpuName} Clock",
                SensorCategory.Gpu,
                "intel.igpu0",
                _gpuName,
                Name,
                SensorAvailability.Unsupported,
                "Integrated GPU clock frequency is not exposed via standard unprivileged Windows WDDM counters."),
            SensorReading.Unavailable(
                "intel.igpu.fan",
                $"{_gpuName} Fan",
                SensorCategory.Cooling,
                "intel.igpu0",
                _gpuName,
                Name,
                SensorAvailability.Unsupported,
                "Integrated GPU shares CPU/chassis cooling and has no independent GPU fan.")
        };

        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    public IReadOnlyList<HardwareInfo> GetDetectedHardware() => _hardware.AsReadOnly();

    public ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        _lastChecked == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _lastChecked,
        0,
        IsAvailable);

    public void Dispose() { }
}
