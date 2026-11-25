using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace CarbonMonitor.Providers.Hardware.LibreHardwareMonitor;

/// <summary>
/// Wraps LibreHardwareMonitorLib (MPL-2.0) for read-only CPU, GPU, motherboard fan, and storage sensors.
/// Strictly read-only: IsControllerEnabled = false (no EC writes or manual fan control).
/// Never relabels CPU package temperature as GPU temperature.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LhmProvider : IHardwareProvider
{
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private readonly UpdateVisitor _visitor = new();
    private readonly List<HardwareInfo> _hardware = new();
    private readonly List<string> _metricIds = new();
    private Computer? _computer;
    private bool _initialized;
    private bool _requiresElevation;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;
    private int _lastSensorCount;

    public string Name => "Hardware.LibreHardwareMonitor";
    public string Description => "Hardware temperatures, clocks, power, and read-only fan RPM via LibreHardwareMonitorLib (MPL-2.0)";
    public IReadOnlyList<string> MetricIds => _metricIds.AsReadOnly();
    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = "LibreHardwareMonitor requires Windows.";
            return Task.FromResult(false);
        }

        try
        {
            _hardware.Clear();
            _metricIds.Clear();

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
                IsMemoryEnabled = true,
                IsControllerEnabled = false, // Safety boundary: no fan/controller writes
                IsNetworkEnabled = false
            };

            _computer.Open();
            _computer.Accept(_visitor);

            foreach (var hw in _computer.Hardware)
            {
                AddHardwareDescriptor(hw);
                foreach (var sub in hw.SubHardware)
                    AddHardwareDescriptor(sub);
            }

            _initialized = true;
            IsAvailable = _hardware.Count > 0;

            bool hasAnyTempOrFan = _computer.Hardware.Any(h =>
                h.Sensors.Any(s => s.SensorType is SensorType.Temperature or SensorType.Fan && s.Value.HasValue) ||
                h.SubHardware.Any(sh => sh.Sensors.Any(s => s.SensorType is SensorType.Temperature or SensorType.Fan && s.Value.HasValue)));

            if (!hasAnyTempOrFan)
            {
                _requiresElevation = true;
                Health = ProviderHealth.Degraded;
                HealthReason = "Hardware topology detected, but thermal/fan MSR/SMBus sensors may require Administrator privileges.";
            }
            else
            {
                Health = ProviderHealth.Healthy;
                HealthReason = null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _requiresElevation = true;
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "Administrator privileges required by LibreHardwareMonitor for low-level sensor access.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"LibreHardwareMonitor init failed: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        _lastChecked = now;

        if (_computer is null || !_initialized)
        {
            if (_requiresElevation)
            {
                readings.Add(SensorReading.Unavailable(
                    "lhm.cpu.temperature.package",
                    "CPU Package Temperature",
                    SensorCategory.Cpu,
                    "cpu0",
                    "CPU",
                    Name,
                    SensorAvailability.PermissionRequired,
                    HealthReason ?? "Administrator privileges required for CPU thermal sensors."));
            }
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        try
        {
            _computer.Accept(_visitor);

            var emittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hw in _computer.Hardware)
            {
                CollectHardwareReadings(hw, readings, emittedIds, now);
                foreach (var sub in hw.SubHardware)
                    CollectHardwareReadings(sub, readings, emittedIds, now);
            }

            // If CPU temperature was still not exposed (non-elevated laptop), emit honest status
            if (!emittedIds.Contains("lhm.cpu.temperature.package"))
            {
                readings.Add(SensorReading.Unavailable(
                    "lhm.cpu.temperature.package",
                    "CPU Package Temperature (MSR)",
                    SensorCategory.Cpu,
                    "cpu0",
                    _hardware.FirstOrDefault(h => h.Category == SensorCategory.Cpu)?.Name ?? "CPU",
                    Name,
                    _requiresElevation ? SensorAvailability.PermissionRequired : SensorAvailability.Unsupported,
                    _requiresElevation
                        ? "Direct CPU Package thermal MSR requires Administrator elevation (ACPI Thermal Zone is used as unprivileged fallback)."
                        : "CPU package temperature sensor was not exposed.",
                    permissionRequirement: _requiresElevation ? "Administrator" : null));
            }
        }
        catch (Exception ex)
        {
            Health = ProviderHealth.Degraded;
            HealthReason = $"LibreHardwareMonitor read error: {ex.Message}";
        }

        _lastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    private void AddHardwareDescriptor(IHardware hw)
    {
        var category = MapCategory(hw.HardwareType);
        var vendor = hw.HardwareType switch
        {
            HardwareType.GpuNvidia => "NVIDIA",
            HardwareType.GpuAmd => "AMD",
            HardwareType.GpuIntel => "Intel",
            _ => "System"
        };

        _hardware.Add(new HardwareInfo(hw.Identifier.ToString(), hw.Name, vendor, null, category));
    }

    private void CollectHardwareReadings(
        IHardware hw,
        List<SensorReading> readings,
        HashSet<string> emittedIds,
        DateTimeOffset now)
    {
        var category = MapCategory(hw.HardwareType);
        var prefix = MapPrefix(hw.HardwareType);
        var hwId = hw.Identifier.ToString();

        foreach (var sensor in hw.Sensors)
        {
            if (sensor.Value is not float rawVal || float.IsNaN(rawVal) || float.IsInfinity(rawVal))
                continue;

            // LHM reports 0.0 W for RAPL CPU power sensors when MSR access is blocked without elevation; do not present 0 W as measured.
            if (hw.HardwareType == HardwareType.Cpu && sensor.SensorType == SensorType.Power && _requiresElevation && rawVal <= 0.01f)
                continue;

            var (unit, typeSlug) = MapSensorUnit(sensor.SensorType);
            if (unit == SensorUnit.None && sensor.SensorType is not SensorType.Factor)
                continue;

            string cleanName = NormalizeSlug(sensor.Name);
            string baseId = $"lhm.{prefix}.{typeSlug}.{cleanName}";
            string metricId = baseId;
            int suffix = 2;
            while (!emittedIds.Add(metricId))
            {
                metricId = $"{baseId}_{suffix++}";
            }

            double convertedVal = sensor.SensorType == SensorType.Throughput
                ? rawVal / (1024.0 * 1024.0)
                : rawVal;

            readings.Add(new SensorReading
            {
                MetricId = metricId,
                DisplayName = $"{hw.Name} {sensor.Name}",
                Value = Math.Round(convertedVal, 1),
                Unit = unit,
                Category = category,
                HardwareId = hwId,
                HardwareName = hw.Name,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Measured,
                QualityNote = _requiresElevation ? "Partial sensors (non-elevated)" : null
            });

            // Also emit canonical aliases for common overlay metrics without ever crossing CPU/GPU boundaries
            EmitCanonicalAliases(hw, sensor, rawVal, unit, hwId, readings, emittedIds, now);
        }
    }

    private void EmitCanonicalAliases(
        IHardware hw,
        ISensor sensor,
        float rawVal,
        SensorUnit unit,
        string hwId,
        List<SensorReading> readings,
        HashSet<string> emittedIds,
        DateTimeOffset now)
    {
        if (hw.HardwareType == HardwareType.Cpu)
        {
            if (sensor.SensorType == SensorType.Temperature &&
                (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                 sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                 sensor.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase)) &&
                emittedIds.Add("lhm.cpu.temperature.package"))
            {
                readings.Add(new SensorReading
                {
                    MetricId = "lhm.cpu.temperature.package",
                    DisplayName = "CPU Package Temp",
                    Value = Math.Round(rawVal, 1),
                    Unit = SensorUnit.Celsius,
                    Category = SensorCategory.Cpu,
                    HardwareId = hwId,
                    HardwareName = hw.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured
                });
            }
            else if (sensor.SensorType == SensorType.Power &&
                     sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) &&
                     emittedIds.Add("lhm.cpu.power"))
            {
                readings.Add(new SensorReading
                {
                    MetricId = "lhm.cpu.power",
                    DisplayName = "CPU Package Power",
                    Value = Math.Round(rawVal, 1),
                    Unit = SensorUnit.Watts,
                    Category = SensorCategory.Cpu,
                    HardwareId = hwId,
                    HardwareName = hw.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured
                });
            }
        }
        else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
        {
            if (sensor.SensorType == SensorType.Temperature &&
                sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                emittedIds.Add("gpu.temperature"))
            {
                readings.Add(new SensorReading
                {
                    MetricId = "gpu.temperature",
                    DisplayName = $"{hw.Name} Temp",
                    Value = Math.Round(rawVal, 1),
                    Unit = SensorUnit.Celsius,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hw.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured
                });
            }
            else if (sensor.SensorType == SensorType.Clock &&
                     sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                     emittedIds.Add("gpu.clock"))
            {
                readings.Add(new SensorReading
                {
                    MetricId = "gpu.clock",
                    DisplayName = $"{hw.Name} Core Clock",
                    Value = Math.Round(rawVal, 0),
                    Unit = SensorUnit.MHz,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hw.Name,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured
                });
            }
        }
        else if (hw.HardwareType == HardwareType.Storage &&
                 sensor.SensorType == SensorType.Temperature &&
                 emittedIds.Add("storage.temperature"))
        {
            readings.Add(new SensorReading
            {
                MetricId = "storage.temperature",
                DisplayName = $"{hw.Name} Temp",
                Value = Math.Round(rawVal, 1),
                Unit = SensorUnit.Celsius,
                Category = SensorCategory.Storage,
                HardwareId = hwId,
                HardwareName = hw.Name,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Measured
            });
        }
        else if (hw.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO &&
                 sensor.SensorType == SensorType.Fan &&
                 rawVal > 0 &&
                 emittedIds.Add("cooling.fan.rpm"))
        {
            readings.Add(new SensorReading
            {
                MetricId = "cooling.fan.rpm",
                DisplayName = $"{sensor.Name} RPM",
                Value = Math.Round(rawVal, 0),
                Unit = SensorUnit.Rpm,
                Category = SensorCategory.Cooling,
                HardwareId = hwId,
                HardwareName = hw.Name,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Measured
            });
        }
    }

    private static (SensorUnit Unit, string Slug) MapSensorUnit(SensorType type) => type switch
    {
        SensorType.Temperature => (SensorUnit.Celsius, "temperature"),
        SensorType.Load => (SensorUnit.Percent, "load"),
        SensorType.Clock => (SensorUnit.MHz, "clock"),
        SensorType.Fan => (SensorUnit.Rpm, "fan"),
        SensorType.Power => (SensorUnit.Watts, "power"),
        SensorType.Data => (SensorUnit.GB, "data"),
        SensorType.SmallData => (SensorUnit.MB, "smalldata"),
        SensorType.Voltage => (SensorUnit.Volts, "voltage"),
        SensorType.Throughput => (SensorUnit.MBPerSecond, "throughput"),
        _ => (SensorUnit.None, type.ToString().ToLowerInvariant())
    };

    private static SensorCategory MapCategory(HardwareType type) => type switch
    {
        HardwareType.Cpu => SensorCategory.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorCategory.Gpu,
        HardwareType.Storage => SensorCategory.Storage,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cooler => SensorCategory.Cooling,
        HardwareType.Memory => SensorCategory.Memory,
        _ => SensorCategory.Cpu
    };

    private static string MapPrefix(HardwareType type) => type switch
    {
        HardwareType.Cpu => "cpu",
        HardwareType.GpuNvidia => "gpu.nvidia",
        HardwareType.GpuAmd => "gpu.amd",
        HardwareType.GpuIntel => "gpu.intel",
        HardwareType.Storage => "storage",
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cooler => "cooling",
        HardwareType.Memory => "memory",
        _ => "other"
    };

    private static string NormalizeSlug(string text)
    {
        var chars = text.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    public IReadOnlyList<HardwareInfo> GetDetectedHardware() => _hardware.AsReadOnly();

    public ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        _lastChecked == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _lastChecked,
        _lastSensorCount,
        IsAvailable);

    public void Dispose()
    {
        if (_computer is not null)
        {
            try { _computer.Close(); } catch { }
            _computer = null;
        }
    }
}
