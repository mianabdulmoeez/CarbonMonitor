using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Windows.Memory;

/// <summary>
/// Reads physical RAM capacity, availability, usage, and utilization percentage via Win32 GlobalMemoryStatusEx.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryProvider : WindowsProviderBase
{
    public override string Name => "Windows.Memory";
    public override string Description => "Physical RAM telemetry via Win32 GlobalMemoryStatusEx";
    public override IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "memory.total",
        "memory.available",
        "memory.used",
        "memory.usage.percent"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private string _ramLabel = "System RAM";

    public override Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DetectedHardware.Clear();
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                double totalGb = status.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                _ramLabel = $"{Math.Round(totalGb, 1):F1} GB Physical RAM";
                DetectedHardware.Add(new HardwareInfo("ram0", _ramLabel, "System", null, SensorCategory.Memory));
                IsAvailable = true;
                Health = ProviderHealth.Healthy;
            }
            else
            {
                IsAvailable = false;
                Health = ProviderHealth.Faulted;
                HealthReason = $"GlobalMemoryStatusEx failed (Win32 error {Marshal.GetLastWin32Error()})";
            }
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = ex.Message;
        }

        LastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public override Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>(4);
        var now = DateTimeOffset.UtcNow;
        LastChecked = now;

        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            double totalGb = status.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double availGb = status.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = Math.Max(0, totalGb - availGb);
            double pct = status.dwMemoryLoad;

            readings.Add(new SensorReading
            {
                MetricId = "memory.total",
                DisplayName = "Total RAM",
                Value = Math.Round(totalGb, 1),
                Unit = SensorUnit.GB,
                Category = SensorCategory.Memory,
                HardwareId = "ram0",
                HardwareName = _ramLabel,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter
            });
            readings.Add(new SensorReading
            {
                MetricId = "memory.available",
                DisplayName = "Available RAM",
                Value = Math.Round(availGb, 1),
                Unit = SensorUnit.GB,
                Category = SensorCategory.Memory,
                HardwareId = "ram0",
                HardwareName = _ramLabel,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter
            });
            readings.Add(new SensorReading
            {
                MetricId = "memory.used",
                DisplayName = "Used RAM",
                Value = Math.Round(usedGb, 1),
                Unit = SensorUnit.GB,
                Category = SensorCategory.Memory,
                HardwareId = "ram0",
                HardwareName = _ramLabel,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Calculated
            });
            readings.Add(new SensorReading
            {
                MetricId = "memory.usage.percent",
                DisplayName = "RAM Usage",
                Value = pct,
                Unit = SensorUnit.Percent,
                Category = SensorCategory.Memory,
                HardwareId = "ram0",
                HardwareName = _ramLabel,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter
            });
        }
        else
        {
            readings.Add(SensorReading.Unavailable(
                "memory.usage.percent", "RAM Usage", SensorCategory.Memory,
                "ram0", _ramLabel, Name, SensorAvailability.Error,
                "GlobalMemoryStatusEx returned false"));
        }

        LastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }
}
