using System.Management;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Hardware.Nvme;

/// <summary>
/// Queries Windows Storage Management WMI (MSFT_PhysicalDisk and MSFT_StorageReliabilityCounter)
/// for drive model, bus type, health status (unprivileged), and SSD temperature/wear (when permitted).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StorageHealthProvider : IStorageTelemetryProvider
{
    private readonly List<HardwareInfo> _hardware = new();
    private readonly List<(string Id, string Name, string HealthText, string BusLabel)> _disks = new();
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;
    private int _lastSensorCount;

    public string Name => "Hardware.Storage.WindowsWmi";
    public string Description => "NVMe/SATA drive health (unprivileged) and SMART temperature/wear via Windows Storage WMI";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "storage.temperature",
        "storage.wear.percent",
        "storage.health.status"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool SupportsTemperature { get; private set; }
    public bool SupportsSmartHealth { get; private set; }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "Windows Storage WMI is Windows-only.";
            return Task.FromResult(false);
        }

        try
        {
            _hardware.Clear();
            _disks.Clear();
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk");

            foreach (ManagementObject disk in searcher.Get())
            {
                var id = disk["DeviceId"]?.ToString() ?? "0";
                var name = disk["FriendlyName"]?.ToString()?.Trim() ?? $"PhysicalDisk {id}";
                int busType = Convert.ToInt32(disk["BusType"] ?? 0);
                int healthVal = Convert.ToInt32(disk["HealthStatus"] ?? 0);

                string busLabel = busType switch
                {
                    17 => "NVMe",
                    11 => "SATA",
                    7 => "USB",
                    _ => $"Bus {busType}"
                };

                string healthText = healthVal switch
                {
                    0 => "Healthy",
                    1 => "Warning",
                    2 => "Unhealthy",
                    _ => "Unknown"
                };

                _disks.Add(($"nvme.{id}", name, healthText, busLabel));
                _hardware.Add(new HardwareInfo($"nvme.{id}", name, "Storage", null, SensorCategory.Storage, $"{busLabel} · {healthText}"));
            }

            IsAvailable = _disks.Count > 0;
            SupportsSmartHealth = IsAvailable;
            Health = IsAvailable ? ProviderHealth.Healthy : ProviderHealth.Degraded;
            if (!IsAvailable)
                HealthReason = "No MSFT_PhysicalDisk entries found.";
        }
        catch (ManagementException ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = $"Windows Storage WMI access unavailable: {ex.Message}";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"Storage health init failed: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        _lastChecked = now;

        if (!IsAvailable)
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());

        // 1. Emit unprivileged physical disk health status
        for (int i = 0; i < _disks.Count; i++)
        {
            var d = _disks[i];
            readings.Add(new SensorReading
            {
                MetricId = i == 0 ? "storage.health.status" : $"storage.disk{i}.health.status",
                DisplayName = $"{d.Name} Health",
                TextValue = $"{d.HealthText} ({d.BusLabel})",
                Unit = SensorUnit.None,
                Category = SensorCategory.Storage,
                HardwareId = d.Id,
                HardwareName = d.Name,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter
            });
        }

        // 2. Query MSFT_StorageReliabilityCounter for SMART temperature & wear
        try
        {
            using var reliabilitySearcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, Temperature, Wear FROM MSFT_StorageReliabilityCounter");

            bool foundTemp = false;
            foreach (ManagementObject counter in reliabilitySearcher.Get())
            {
                var id = counter["DeviceId"]?.ToString() ?? "0";
                var hwName = _disks.FirstOrDefault(h => h.Id == $"nvme.{id}").Name ?? $"Disk {id}";

                if (counter["Temperature"] is object tempObj && Convert.ToDouble(tempObj) is double temp && temp is > 0 and < 150)
                {
                    foundTemp = true;
                    SupportsTemperature = true;
                    readings.Add(new SensorReading
                    {
                        MetricId = id == "0" ? "storage.temperature" : $"storage.disk{id}.temperature",
                        DisplayName = $"{hwName} Temperature",
                        Value = temp,
                        Unit = SensorUnit.Celsius,
                        Category = SensorCategory.Storage,
                        HardwareId = $"nvme.{id}",
                        HardwareName = hwName,
                        ProviderName = Name,
                        Timestamp = now,
                        Availability = SensorAvailability.Available,
                        SourceType = SensorSourceType.Measured
                    });
                }

                if (counter["Wear"] is object wearObj && Convert.ToDouble(wearObj) is double wear && wear is >= 0 and <= 100)
                {
                    readings.Add(new SensorReading
                    {
                        MetricId = id == "0" ? "storage.wear.percent" : $"storage.disk{id}.wear",
                        DisplayName = $"{hwName} Wear",
                        Value = wear,
                        Unit = SensorUnit.Percent,
                        Category = SensorCategory.Storage,
                        HardwareId = $"nvme.{id}",
                        HardwareName = hwName,
                        ProviderName = Name,
                        Timestamp = now,
                        Availability = SensorAvailability.Available,
                        SourceType = SensorSourceType.Measured
                    });
                }
            }

            if (!foundTemp)
            {
                var firstDisk = _disks.FirstOrDefault();
                readings.Add(SensorReading.Unavailable(
                    "storage.temperature",
                    $"{firstDisk.Name ?? "SSD"} Temperature",
                    SensorCategory.Storage,
                    firstDisk.Id ?? "nvme.0",
                    firstDisk.Name ?? "Storage",
                    Name,
                    SensorAvailability.PermissionRequired,
                    "NVMe SMART temperature via MSFT_StorageReliabilityCounter requires Administrator elevation on Windows 11."));
            }
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied ||
                                             ex.Message.Contains("Access", StringComparison.OrdinalIgnoreCase))
        {
            var firstDisk = _disks.FirstOrDefault();
            readings.Add(SensorReading.Unavailable(
                "storage.temperature",
                $"{firstDisk.Name ?? "SSD"} Temperature",
                SensorCategory.Storage,
                firstDisk.Id ?? "nvme.0",
                firstDisk.Name ?? "Storage",
                Name,
                SensorAvailability.PermissionRequired,
                "Windows Storage WMI reliability counters require Administrator elevation to read NVMe SMART temperature."));
        }
        catch (UnauthorizedAccessException)
        {
            readings.Add(SensorReading.Unavailable(
                "storage.temperature", "SSD Temperature", SensorCategory.Storage,
                "nvme.0", "Storage", Name, SensorAvailability.PermissionRequired,
                "Administrator privilege required to query MSFT_StorageReliabilityCounter."));
        }
        catch (Exception ex)
        {
            readings.Add(SensorReading.Unavailable(
                "storage.temperature", "SSD Temperature", SensorCategory.Storage,
                "nvme.0", "Storage", Name, SensorAvailability.TemporarilyUnavailable,
                $"Storage WMI query failed: {ex.Message}"));
        }

        _lastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    public IReadOnlyList<HardwareInfo> GetDetectedHardware() => _hardware.AsReadOnly();

    public ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        _lastChecked == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _lastChecked,
        _lastSensorCount,
        IsAvailable);

    public void Dispose() { }
}
