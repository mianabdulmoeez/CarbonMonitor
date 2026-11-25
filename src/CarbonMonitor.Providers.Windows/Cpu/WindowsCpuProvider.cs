using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using Microsoft.Win32;

namespace CarbonMonitor.Providers.Windows.Cpu;

/// <summary>
/// Reads CPU identity, overall & per-core utilization, live dynamic clock frequency,
/// and unprivileged Windows ACPI Thermal Zone temperature via PDH/WMI/Registry.
/// Never mislabels ACPI thermal zone as direct Intel MSR package temp or GPU temp.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuProvider : WindowsProviderBase
{
    private PerformanceCounter? _cpuTotalCounter;
    private PerformanceCounter? _freqCounter;
    private PerformanceCounter? _perfPctCounter;
    private PerformanceCounter? _thermalZoneCounter;

    private Dictionary<string, CounterSample> _prevCoreSamples = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasProcessorInfoCategory;
    private string _cpuName = "Unknown CPU";
    private int _coreCount;
    private int _logicalCount;
    private int _nominalClockMHz;
    private string _thermalInstanceName = @"\_TZ.THM0";

    public override string Name => "Windows.Cpu";
    public override string Description => "CPU utilization (total & per-core), live clock frequency, and ACPI thermal zone via Windows PDH/WMI";
    public override IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "cpu.usage.total",
        "cpu.clock.current",
        "cpu.temperature.thermalzone"
    };

    public override async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                DetectedHardware.Clear();
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    _cpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                    _coreCount = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    _logicalCount = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                    _nominalClockMHz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);

                    DetectedHardware.Add(new HardwareInfo(
                        "cpu0",
                        _cpuName,
                        _cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD" :
                        _cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "System",
                        null,
                        SensorCategory.Cpu,
                        $"{_coreCount}C/{_logicalCount}T (Nominal {_nominalClockMHz} MHz)"));
                    break;
                }

                _hasProcessorInfoCategory = PerformanceCounterCategory.Exists("Processor Information");
                if (_hasProcessorInfoCategory)
                {
                    try
                    {
                        _cpuTotalCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                        _cpuTotalCounter.NextValue();

                        _freqCounter = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                        _freqCounter.NextValue();

                        _perfPctCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                        _perfPctCounter.NextValue();

                        PrimePerCoreSamples();
                    }
                    catch (Exception ex)
                    {
                        HealthReason = $"Processor Information PDH partial: {ex.Message}";
                    }
                }

                if (_cpuTotalCounter is null)
                {
                    try
                    {
                        _cpuTotalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                        _cpuTotalCounter.NextValue();
                    }
                    catch (Exception ex)
                    {
                        HealthReason = $"CPU performance counter unavailable: {ex.Message}";
                    }
                }

                try
                {
                    if (PerformanceCounterCategory.Exists("Thermal Zone Information"))
                    {
                        var tzCat = new PerformanceCounterCategory("Thermal Zone Information");
                        var instances = tzCat.GetInstanceNames();
                        if (instances.Length > 0)
                        {
                            _thermalInstanceName = instances[0];
                            _thermalZoneCounter = new PerformanceCounter("Thermal Zone Information", "Temperature", _thermalInstanceName, true);
                            _thermalZoneCounter.NextValue();
                        }
                    }
                }
                catch
                {
                    _thermalZoneCounter = null;
                }

                IsAvailable = true;
                Health = _cpuTotalCounter is not null ? ProviderHealth.Healthy : ProviderHealth.Degraded;
                LastChecked = DateTimeOffset.UtcNow;
            }, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            Health = ProviderHealth.Faulted;
            HealthReason = $"CPU provider initialization failed: {ex.Message}";
            IsAvailable = false;
            LastChecked = DateTimeOffset.UtcNow;
            return false;
        }
    }

    private void PrimePerCoreSamples()
    {
        try
        {
            var cat = new PerformanceCounterCategory("Processor Information");
            var data = cat.ReadCategory();
            var colName = data.Contains("% Processor Utility") ? "% Processor Utility" :
                          data.Contains("% Processor Time") ? "% Processor Time" : null;
            if (colName is null) return;

            var col = data[colName];
            foreach (InstanceData inst in col.Values)
            {
                if (IsLogicalCoreInstance(inst.InstanceName))
                    _prevCoreSamples[inst.InstanceName] = inst.Sample;
            }
        }
        catch
        {
            // Optional per-core breakdown
        }
    }

    public override Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        LastChecked = now;

        // 1. Overall CPU Usage
        if (_cpuTotalCounter is not null)
        {
            try
            {
                var usage = Math.Clamp(_cpuTotalCounter.NextValue(), 0f, 100f);
                readings.Add(new SensorReading
                {
                    MetricId = "cpu.usage.total",
                    DisplayName = "CPU Usage",
                    Value = Math.Round(usage, 1),
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Cpu,
                    HardwareId = "cpu0",
                    HardwareName = _cpuName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
            }
            catch (Exception ex)
            {
                readings.Add(SensorReading.Unavailable(
                    "cpu.usage.total", "CPU Usage", SensorCategory.Cpu,
                    "cpu0", _cpuName, Name, SensorAvailability.TemporarilyUnavailable,
                    $"CPU counter read failed: {ex.Message}"));
            }
        }
        else
        {
            readings.Add(SensorReading.Unavailable(
                "cpu.usage.total", "CPU Usage", SensorCategory.Cpu,
                "cpu0", _cpuName, Name, SensorAvailability.Unsupported,
                HealthReason ?? "Windows CPU performance counter unavailable"));
        }

        // 2. Live Effective CPU Clock (using Processor Frequency & % Processor Performance, falling back to Registry ~MHz)
        AddLiveClockReading(readings, now);

        // 3. Unprivileged Windows ACPI Thermal Zone temperature (with HighPrecision WMI fallback)
        AddThermalZoneReading(readings, now);

        // 4. Per-core / per-thread utilization via PDH ReadCategory
        AddPerCoreReadings(readings, now);

        LastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    private void AddLiveClockReading(List<SensorReading> readings, DateTimeOffset now)
    {
        try
        {
            int regBase = 0;
            using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                if (key?.GetValue("~MHz") is int mhz) regBase = mhz;
            }

            int baseClock = _nominalClockMHz > 0 ? _nominalClockMHz : regBase;
            double? liveMhz = null;
            string note = "Windows PDH live effective frequency";

            if (_perfPctCounter is not null && baseClock > 0)
            {
                double pct = _perfPctCounter.NextValue();
                if (pct > 1)
                {
                    liveMhz = baseClock * (pct / 100.0);
                    note = $"Windows PDH effective turbo frequency ({pct:F0}% of {baseClock} MHz base)";
                }
            }

            if ((liveMhz is null || liveMhz <= 0) && _freqCounter is not null)
            {
                double pdhFreq = _freqCounter.NextValue();
                if (pdhFreq > 0)
                {
                    liveMhz = pdhFreq;
                    note = "Windows PDH Processor Frequency";
                }
            }

            if ((liveMhz is null || liveMhz <= 0) && regBase > 0)
            {
                liveMhz = regBase;
                note = "Windows Registry nominal ~MHz";
            }

            if (liveMhz is double clk && clk > 0)
            {
                readings.Add(new SensorReading
                {
                    MetricId = "cpu.clock.current",
                    DisplayName = "CPU Clock",
                    Value = Math.Round(clk, 0),
                    Unit = SensorUnit.MHz,
                    Category = SensorCategory.Cpu,
                    HardwareId = "cpu0",
                    HardwareName = _cpuName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter,
                    QualityNote = note
                });
                return;
            }
        }
        catch
        {
            // Fall through to unavailable
        }

        readings.Add(SensorReading.Unavailable(
            "cpu.clock.current", "CPU Clock", SensorCategory.Cpu,
            "cpu0", _cpuName, Name, SensorAvailability.TemporarilyUnavailable,
            "Could not query live CPU clock frequency."));
    }

    private void AddThermalZoneReading(List<SensorReading> readings, DateTimeOffset now)
    {
        double? celsius = null;
        string sourceNote = $"Windows ACPI Thermal Zone ({_thermalInstanceName}); unprivileged OS counter reflecting platform/CPU thermal zone (not direct per-core MSR).";

        try
        {
            // Prefer HighPrecisionTemperature (tenths of Kelvin) via Win32_PerfFormattedData_Counters_ThermalZoneInformation when available
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            foreach (ManagementObject obj in searcher.Get())
            {
                var tzName = obj["Name"]?.ToString() ?? _thermalInstanceName;
                if (obj["HighPrecisionTemperature"] is uint hp && hp > 2000 && hp < 4500)
                {
                    celsius = (hp / 10.0) - 273.15;
                    _thermalInstanceName = tzName;
                    break;
                }
                if (obj["Temperature"] is uint k && k > 200 && k < 450)
                {
                    celsius = k - 273.15;
                    _thermalInstanceName = tzName;
                    break;
                }
            }
        }
        catch
        {
            // Fall back to PDH counter
        }

        if (celsius is null && _thermalZoneCounter is not null)
        {
            try
            {
                float kelvin = _thermalZoneCounter.NextValue();
                if (kelvin is > 200 and < 450)
                    celsius = kelvin - 273.15;
            }
            catch
            {
                // Ignore
            }
        }

        if (celsius is double tempC && tempC is > -10 and < 130)
        {
            readings.Add(new SensorReading
            {
                MetricId = "cpu.temperature.thermalzone",
                DisplayName = $"System / CPU Thermal Zone ({_thermalInstanceName})",
                Value = Math.Round(tempC, 1),
                Unit = SensorUnit.Celsius,
                Category = SensorCategory.Cpu,
                HardwareId = "cpu0",
                HardwareName = _cpuName,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.OsCounter,
                QualityNote = sourceNote
            });
        }
        else
        {
            readings.Add(SensorReading.Unavailable(
                "cpu.temperature.thermalzone", "CPU Thermal Zone", SensorCategory.Cpu,
                "cpu0", _cpuName, Name, SensorAvailability.Unsupported,
                "Firmware did not expose an unprivileged ACPI Thermal Zone counter."));
        }
    }

    private void AddPerCoreReadings(List<SensorReading> readings, DateTimeOffset now)
    {
        if (!_hasProcessorInfoCategory) return;

        try
        {
            var cat = new PerformanceCounterCategory("Processor Information");
            var data = cat.ReadCategory();
            var colName = data.Contains("% Processor Utility") ? "% Processor Utility" :
                          data.Contains("% Processor Time") ? "% Processor Time" : null;
            if (colName is null) return;

            var col = data[colName];
            var nextSamples = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
            var coreList = new List<(string Name, double Value)>();

            foreach (InstanceData inst in col.Values)
            {
                if (!IsLogicalCoreInstance(inst.InstanceName))
                    continue;

                nextSamples[inst.InstanceName] = inst.Sample;
                if (_prevCoreSamples.TryGetValue(inst.InstanceName, out var prev))
                {
                    double val = Math.Clamp(CounterSample.Calculate(prev, inst.Sample), 0, 100);
                    coreList.Add((inst.InstanceName, val));
                }
            }

            _prevCoreSamples = nextSamples;

            int idx = 0;
            foreach (var core in coreList.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"cpu.core.{idx}",
                    DisplayName = $"CPU Thread {idx} ({core.Name})",
                    Value = Math.Round(core.Value, 1),
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Cpu,
                    HardwareId = "cpu0",
                    HardwareName = _cpuName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
                idx++;
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private static bool IsLogicalCoreInstance(string name)
        => !name.Contains("_Total", StringComparison.OrdinalIgnoreCase) && name.Contains(',');

    public override void Dispose()
    {
        _cpuTotalCounter?.Dispose();
        _freqCounter?.Dispose();
        _perfPctCounter?.Dispose();
        _thermalZoneCounter?.Dispose();
        _cpuTotalCounter = null;
        _freqCounter = null;
        _perfPctCounter = null;
        _thermalZoneCounter = null;
    }
}
