using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;

namespace CarbonMonitor.Providers.Windows.Gpu;

/// <summary>
/// Reads Windows 10/11 WDDM GPU Engine utilization (total & per-engine) and GPU Adapter Memory
/// (dedicated vs shared) via Performance Counters, plus adapter discovery via Win32_VideoController.
/// Uses canonical PCI/PnP identifiers so multiple providers do not duplicate the same physical GPU.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGpuEngineProvider : WindowsProviderBase, IGpuTelemetryProvider
{
    private sealed record AdapterDescriptor(
        string HardwareId,
        string CanonicalId,
        string Name,
        string Vendor,
        string? DriverVersion,
        string? PnpDeviceId,
        bool IsIntegrated,
        int Index);

    private readonly List<AdapterDescriptor> _adapters = new();
    private Dictionary<string, CounterSample> _prevEngineSamples = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasGpuEngineCounters;
    private bool _hasGpuMemoryCounters;

    public override string Name => "Windows.GpuEngine";
    public override string Description => "Windows WDDM GPU Engine utilization, per-engine loads, and dedicated/shared memory counters";
    public override IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "gpu.usage",
        "gpu.memory.dedicated",
        "gpu.memory.shared",
        "gpu.engine.3d",
        "gpu.engine.videodecode",
        "gpu.engine.copy",
        "gpu.engine.videoprocessing"
    };

    public bool SupportsTemperature => false;
    public bool SupportsClock => false;
    public bool SupportsMemoryUsage => _hasGpuMemoryCounters;
    public bool SupportsFanSpeed => false;
    public string GpuType => _adapters.Any(a => !a.IsIntegrated) ? "Hybrid/Dedicated" : "Integrated";

    public override async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                _adapters.Clear();
                DetectedHardware.Clear();

                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                int idx = 0;
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim() ?? $"GPU {idx}";
                    var vendor = obj["AdapterCompatibility"]?.ToString()?.Trim() ?? DetectVendor(name);
                    var driver = obj["DriverVersion"]?.ToString()?.Trim();
                    var pnp = obj["PNPDeviceID"]?.ToString()?.Trim();
                    bool integrated = IsIntegratedAdapter(name, vendor);
                    var canonicalId = GpuReadingAggregator.ResolveCanonicalKey(name, pnp);

                    var hwId = $"win.gpu{idx}";
                    _adapters.Add(new AdapterDescriptor(hwId, canonicalId, name, vendor, driver, pnp, integrated, idx));
                    DetectedHardware.Add(new HardwareInfo(
                        hwId,
                        name,
                        vendor,
                        driver,
                        SensorCategory.Gpu,
                        integrated ? "Integrated GPU" : "Dedicated GPU",
                        pnp,
                        canonicalId));
                    idx++;
                }

                _hasGpuEngineCounters = PerformanceCounterCategory.Exists("GPU Engine");
                _hasGpuMemoryCounters = PerformanceCounterCategory.Exists("GPU Adapter Memory");

                if (_hasGpuEngineCounters)
                {
                    try
                    {
                        var cat = new PerformanceCounterCategory("GPU Engine");
                        var data = cat.ReadCategory();
                        if (data.Contains("Utilization Percentage"))
                        {
                            var col = data["Utilization Percentage"];
                            foreach (InstanceData inst in col.Values)
                            {
                                _prevEngineSamples[inst.InstanceName] = inst.Sample;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _hasGpuEngineCounters = false;
                        HealthReason = $"GPU Engine PDH read failed: {ex.Message}";
                    }
                }

                IsAvailable = _adapters.Count > 0;
                Health = IsAvailable && (_hasGpuEngineCounters || _hasGpuMemoryCounters)
                    ? ProviderHealth.Healthy
                    : ProviderHealth.Degraded;

                if (!_hasGpuEngineCounters && string.IsNullOrEmpty(HealthReason))
                    HealthReason = "Windows GPU Engine counters require Windows 10 1709+ WDDM 2.x.";

                LastChecked = DateTimeOffset.UtcNow;
            }, cancellationToken).ConfigureAwait(false);

            return IsAvailable;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"Windows GPU Engine init failed: {ex.Message}";
            LastChecked = DateTimeOffset.UtcNow;
            return false;
        }
    }

    public override Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        LastChecked = now;

        if (_adapters.Count == 0)
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());

        var luidEngines = ReadUtilizationByLuidAndEngine();
        var luidMemory = ReadMemoryByLuid();
        var sortedLuids = luidEngines.Keys.Union(luidMemory.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < _adapters.Count; i++)
        {
            var adapter = _adapters[i];
            string? matchedLuid = i < sortedLuids.Count ? sortedLuids[i] : sortedLuids.FirstOrDefault();

            string usageId = i == 0 ? "gpu.usage" : $"gpu.{i}.usage";
            string dedMemId = i == 0 ? "gpu.memory.dedicated" : $"gpu.{i}.memory.dedicated";
            string sharedMemId = i == 0 ? "gpu.memory.shared" : $"gpu.{i}.memory.shared";
            string prefix = i == 0 ? "gpu.engine" : $"gpu.{i}.engine";

            if (_hasGpuEngineCounters && matchedLuid is not null && luidEngines.TryGetValue(matchedLuid, out var engines))
            {
                double maxEngine = engines.Values.Count > 0 ? Math.Min(100.0, engines.Values.Max()) : 0.0;
                readings.Add(new SensorReading
                {
                    MetricId = usageId,
                    DisplayName = $"{adapter.Name} Utilization",
                    Value = Math.Round(maxEngine, 1),
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Gpu,
                    HardwareId = adapter.HardwareId,
                    HardwareName = adapter.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter,
                    QualityNote = "Windows WDDM GPU Engine peak engine utilization"
                });

                foreach (var engType in new[] { ("3D", "3d"), ("VideoDecode", "videodecode"), ("Copy", "copy"), ("VideoProcessing", "videoprocessing") })
                {
                    engines.TryGetValue(engType.Item1, out var engVal);
                    readings.Add(new SensorReading
                    {
                        MetricId = $"{prefix}.{engType.Item2}",
                        DisplayName = $"{adapter.Name} {engType.Item1}",
                        Value = Math.Round(Math.Clamp(engVal, 0, 100), 1),
                        Unit = SensorUnit.Percent,
                        Category = SensorCategory.Gpu,
                        HardwareId = adapter.HardwareId,
                        HardwareName = adapter.Name,
                        ProviderName = Name,
                        Timestamp = now,
                        Availability = SensorAvailability.Available,
                        SourceType = SensorSourceType.OsCounter
                    });
                }
            }
            else
            {
                readings.Add(SensorReading.Unavailable(
                    usageId, $"{adapter.Name} Utilization", SensorCategory.Gpu,
                    adapter.HardwareId, adapter.Name, Name,
                    _hasGpuEngineCounters ? SensorAvailability.TemporarilyUnavailable : SensorAvailability.Unsupported,
                    HealthReason ?? "No active GPU Engine counter samples for adapter"));
            }

            if (_hasGpuMemoryCounters && matchedLuid is not null && luidMemory.TryGetValue(matchedLuid, out var mem))
            {
                if (adapter.IsIntegrated)
                {
                    readings.Add(SensorReading.Unavailable(
                        dedMemId, $"{adapter.Name} Dedicated VRAM", SensorCategory.Gpu,
                        adapter.HardwareId, adapter.Name, Name,
                        SensorAvailability.Unsupported,
                        "Integrated GPU shares system RAM instead of dedicated VRAM."));
                }
                else
                {
                    readings.Add(new SensorReading
                    {
                        MetricId = dedMemId,
                        DisplayName = $"{adapter.Name} Dedicated VRAM",
                        Value = Math.Round(mem.dedicatedMb, 1),
                        Unit = SensorUnit.MB,
                        Category = SensorCategory.Gpu,
                        HardwareId = adapter.HardwareId,
                        HardwareName = adapter.Name,
                        ProviderName = Name,
                        Timestamp = now,
                        Availability = SensorAvailability.Available,
                        SourceType = SensorSourceType.OsCounter
                    });
                }

                readings.Add(new SensorReading
                {
                    MetricId = sharedMemId,
                    DisplayName = $"{adapter.Name} Shared GPU Memory",
                    Value = Math.Round(mem.sharedMb, 1),
                    Unit = SensorUnit.MB,
                    Category = SensorCategory.Gpu,
                    HardwareId = adapter.HardwareId,
                    HardwareName = adapter.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
            }
            else
            {
                readings.Add(SensorReading.Unavailable(
                    dedMemId, $"{adapter.Name} Dedicated VRAM", SensorCategory.Gpu,
                    adapter.HardwareId, adapter.Name, Name, SensorAvailability.Unsupported,
                    "Windows GPU Adapter Memory counter unavailable"));
                readings.Add(SensorReading.Unavailable(
                    sharedMemId, $"{adapter.Name} Shared GPU Memory", SensorCategory.Gpu,
                    adapter.HardwareId, adapter.Name, Name, SensorAvailability.Unsupported,
                    "Windows GPU Adapter Memory counter unavailable"));
            }
        }

        LastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    private Dictionary<string, Dictionary<string, double>> ReadUtilizationByLuidAndEngine()
    {
        var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        if (!_hasGpuEngineCounters) return result;

        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            var data = cat.ReadCategory();
            if (!data.Contains("Utilization Percentage"))
                return result;

            var nextSamples = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
            var col = data["Utilization Percentage"];

            foreach (InstanceData inst in col.Values)
            {
                var name = inst.InstanceName;
                nextSamples[name] = inst.Sample;

                if (_prevEngineSamples.TryGetValue(name, out var prev))
                {
                    float val = CounterSample.Calculate(prev, inst.Sample);
                    var luid = ExtractLuidKey(name);
                    if (luid is not null)
                    {
                        var engType = ExtractEngineType(name);
                        if (!result.TryGetValue(luid, out var engMap))
                        {
                            engMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            result[luid] = engMap;
                        }
                        engMap.TryGetValue(engType, out var cur);
                        engMap[engType] = Math.Min(100.0, cur + Math.Max(0, val));
                    }
                }
            }

            _prevEngineSamples = nextSamples;
        }
        catch
        {
            // Graceful fallback
        }

        return result;
    }

    private Dictionary<string, (double dedicatedMb, double sharedMb)> ReadMemoryByLuid()
    {
        var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        if (!_hasGpuMemoryCounters) return result;

        try
        {
            var cat = new PerformanceCounterCategory("GPU Adapter Memory");
            var data = cat.ReadCategory();
            var dedCol = data.Contains("Dedicated Usage") ? data["Dedicated Usage"] : null;
            var sharedCol = data.Contains("Shared Usage") ? data["Shared Usage"] : null;

            if (dedCol is not null)
            {
                foreach (InstanceData inst in dedCol.Values)
                {
                    var luid = ExtractLuidKey(inst.InstanceName) ?? inst.InstanceName;
                    double dedMb = inst.Sample.RawValue / (1024.0 * 1024.0);
                    double sharedMb = 0;
                    if (sharedCol is not null && sharedCol.Contains(inst.InstanceName))
                    {
                        sharedMb = sharedCol[inst.InstanceName]!.Sample.RawValue / (1024.0 * 1024.0);
                    }
                    result[luid] = (dedMb, sharedMb);
                }
            }
        }
        catch
        {
            // Graceful fallback
        }

        return result;
    }

    private static string? ExtractLuidKey(string instanceName)
    {
        var match = Regex.Match(instanceName, @"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+_phys_\d+");
        return match.Success ? match.Value : null;
    }

    private static string ExtractEngineType(string instanceName)
    {
        int idx = instanceName.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? instanceName[(idx + 8)..] : "Other";
    }

    private static string DetectVendor(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return "Unknown";
    }

    private static bool IsIntegratedAdapter(string name, string vendor)
    {
        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
               (name.Contains("Vega", StringComparison.OrdinalIgnoreCase) && name.Contains("Graphics", StringComparison.OrdinalIgnoreCase));
    }
}
