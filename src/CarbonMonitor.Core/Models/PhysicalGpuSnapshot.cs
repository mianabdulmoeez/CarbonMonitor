namespace CarbonMonitor.Core.Models;

/// <summary>
/// Represents a single deduplicated physical GPU adapter with merged telemetry across providers.
/// </summary>
public sealed class PhysicalGpuSnapshot
{
    public required string CanonicalKey { get; init; }
    public required string Name { get; set; }
    public string Vendor { get; set; } = "Unknown";
    public string? DriverVersion { get; set; }
    public string? PnpDeviceId { get; set; }
    public bool IsIntegrated { get; set; }
    public bool IsVirtualOrSoftware { get; set; }

    public SensorReading? Utilization { get; set; }
    public SensorReading? Temperature { get; set; }
    public SensorReading? CoreClock { get; set; }
    public SensorReading? MemoryClock { get; set; }
    public SensorReading? DedicatedVramUsed { get; set; }
    public SensorReading? DedicatedVramTotal { get; set; }
    public SensorReading? SharedMemoryUsed { get; set; }
    public SensorReading? SharedMemoryTotal { get; set; }
    public SensorReading? FanSpeed { get; set; }
    public SensorReading? PowerDraw { get; set; }

    public List<SensorReading> EngineReadings { get; } = new();
    public List<SensorReading> AllReadings { get; } = new();
    public HashSet<string> ContributingProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
}
