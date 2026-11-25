using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Windows.Disk;

/// <summary>
/// Reads fixed drive capacity, per-volume breakdown, and PhysicalDisk read/write/active counters.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskProvider : WindowsProviderBase
{
    private PerformanceCounter? _readCounter;
    private PerformanceCounter? _writeCounter;
    private PerformanceCounter? _activeTimeCounter;
    private string _primaryDiskName = "System Storage";

    public override string Name => "Windows.Disk";
    public override string Description => "Storage capacity, volume breakdown, and disk read/write activity via Windows DriveInfo & PDH";
    public override IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "disk.usage.percent",
        "disk.used.gb",
        "disk.total.gb",
        "disk.volumes",
        "disk.read.speed",
        "disk.write.speed",
        "disk.active.percent"
    };

    public override async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                DetectedHardware.Clear();
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT DeviceID, Model, InterfaceType, PNPDeviceID, Size FROM Win32_DiskDrive");
                    int idx = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var model = obj["Model"]?.ToString()?.Trim() ?? $"Disk {idx}";
                        var iface = obj["InterfaceType"]?.ToString() ?? "Unknown";
                        var pnp = obj["PNPDeviceID"]?.ToString()?.Trim();
                        string busLabel = (pnp?.Contains("NVME", StringComparison.OrdinalIgnoreCase) ?? false) ? "NVMe" : iface;
                        if (idx == 0) _primaryDiskName = model;
                        DetectedHardware.Add(new HardwareInfo(
                            $"disk{idx}",
                            model,
                            "Storage",
                            null,
                            SensorCategory.Storage,
                            $"Interface: {busLabel}",
                            pnp));
                        idx++;
                    }
                }
                catch
                {
                    DetectedHardware.Add(new HardwareInfo("disk0", _primaryDiskName, "Storage", null, SensorCategory.Storage));
                }

                try
                {
                    _readCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                    _writeCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                    _activeTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                    _readCounter.NextValue();
                    _writeCounter.NextValue();
                    _activeTimeCounter.NextValue();
                }
                catch (Exception ex)
                {
                    HealthReason = $"PhysicalDisk PDH counters unavailable: {ex.Message}";
                }

                IsAvailable = true;
                Health = _readCounter is not null ? ProviderHealth.Healthy : ProviderHealth.Degraded;
                LastChecked = DateTimeOffset.UtcNow;
            }, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = ex.Message;
            LastChecked = DateTimeOffset.UtcNow;
            return false;
        }
    }

    public override Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        LastChecked = now;

        try
        {
            var fixedDrives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
            if (fixedDrives.Count > 0)
            {
                long totalBytes = fixedDrives.Sum(d => d.TotalSize);
                long freeBytes = fixedDrives.Sum(d => d.TotalFreeSpace);
                long usedBytes = Math.Max(0, totalBytes - freeBytes);
                double usedPct = totalBytes > 0 ? usedBytes * 100.0 / totalBytes : 0;

                double usedGb = usedBytes / (1024.0 * 1024.0 * 1024.0);
                double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);

                readings.Add(new SensorReading
                {
                    MetricId = "disk.usage.percent",
                    DisplayName = "Disk Capacity Used %",
                    Value = Math.Round(usedPct, 1),
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Storage,
                    HardwareId = "disk0",
                    HardwareName = _primaryDiskName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
                readings.Add(new SensorReading
                {
                    MetricId = "disk.used.gb",
                    DisplayName = "Disk Used Capacity",
                    Value = Math.Round(usedGb, 1),
                    Unit = SensorUnit.GB,
                    Category = SensorCategory.Storage,
                    HardwareId = "disk0",
                    HardwareName = _primaryDiskName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
                readings.Add(new SensorReading
                {
                    MetricId = "disk.total.gb",
                    DisplayName = "Disk Total Capacity",
                    Value = Math.Round(totalGb, 1),
                    Unit = SensorUnit.GB,
                    Category = SensorCategory.Storage,
                    HardwareId = "disk0",
                    HardwareName = _primaryDiskName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });

                var volSummary = string.Join("  ·  ", fixedDrives.Select(d =>
                {
                    double u = (d.TotalSize - d.TotalFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                    double t = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    return $"{d.Name.TrimEnd('\\')} {u:F0}/{t:F0} GB";
                }));
                readings.Add(new SensorReading
                {
                    MetricId = "disk.volumes",
                    DisplayName = "Logical Volumes",
                    TextValue = volSummary,
                    Unit = SensorUnit.None,
                    Category = SensorCategory.Storage,
                    HardwareId = "disk0",
                    HardwareName = _primaryDiskName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
            }
        }
        catch (Exception ex)
        {
            readings.Add(SensorReading.Unavailable(
                "disk.usage.percent", "Disk Capacity Used", SensorCategory.Storage,
                "disk0", _primaryDiskName, Name, SensorAvailability.TemporarilyUnavailable, ex.Message));
        }

        ReadThroughputCounter(_readCounter, "disk.read.speed", "Disk Read Speed", SensorUnit.MBPerSecond, 1.0 / (1024.0 * 1024.0), readings, now);
        ReadThroughputCounter(_writeCounter, "disk.write.speed", "Disk Write Speed", SensorUnit.MBPerSecond, 1.0 / (1024.0 * 1024.0), readings, now);
        ReadThroughputCounter(_activeTimeCounter, "disk.active.percent", "Disk Active Time", SensorUnit.Percent, 1.0, readings, now, clamp100: true);

        LastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    private void ReadThroughputCounter(
        PerformanceCounter? counter,
        string metricId,
        string displayName,
        SensorUnit unit,
        double scale,
        List<SensorReading> readings,
        DateTimeOffset now,
        bool clamp100 = false)
    {
        if (counter is null)
        {
            readings.Add(SensorReading.Unavailable(
                metricId, displayName, SensorCategory.Storage,
                "disk0", _primaryDiskName, Name, SensorAvailability.Unsupported,
                HealthReason ?? "PhysicalDisk PDH counter unavailable"));
            return;
        }

        try
        {
            double val = counter.NextValue() * scale;
            if (clamp100) val = Math.Clamp(val, 0, 100);
            readings.Add(new SensorReading
            {
                MetricId = metricId,
                DisplayName = displayName,
                Value = Math.Round(val, 2),
                Unit = unit,
                Category = SensorCategory.Storage,
                HardwareId = "disk0",
                HardwareName = _primaryDiskName,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter
            });
        }
        catch (Exception ex)
        {
            readings.Add(SensorReading.Unavailable(
                metricId, displayName, SensorCategory.Storage,
                "disk0", _primaryDiskName, Name, SensorAvailability.TemporarilyUnavailable, ex.Message));
        }
    }

    public override void Dispose()
    {
        _readCounter?.Dispose();
        _writeCounter?.Dispose();
        _activeTimeCounter?.Dispose();
        _readCounter = null;
        _writeCounter = null;
        _activeTimeCounter = null;
    }
}
