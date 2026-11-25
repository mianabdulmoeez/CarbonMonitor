using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Hardware.Amd;

/// <summary>
/// Capability-detection provider for AMD Radeon integrated and dedicated GPUs (ADLX/ADL).
/// Never bundles proprietary AMD binaries; detects driver DLLs and falls back cleanly to
/// Windows.GpuEngine / LibreHardwareMonitor when AMD ADLX bindings or hardware are absent.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmdAdlxProvider : IGpuTelemetryProvider
{
    private readonly List<HardwareInfo> _hardware = new();
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;
    private bool _amdDriverDllPresent;

    public string Name => "Hardware.Amd.ADLX";
    public string Description => "AMD Radeon ADLX capability probe (requires AMD Radeon drivers and ADLX SDK bindings)";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "amd.adlx.status"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }

    public bool SupportsTemperature => false;
    public bool SupportsClock => false;
    public bool SupportsMemoryUsage => false;
    public bool SupportsFanSpeed => false;
    public string GpuType => "AMD";

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "AMD ADLX requires Windows.";
            return Task.FromResult(false);
        }

        try
        {
            _hardware.Clear();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion FROM Win32_VideoController");
            int idx = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                {
                    _hardware.Add(new HardwareInfo(
                        $"amd.gpu{idx++}",
                        name,
                        "AMD",
                        obj["DriverVersion"]?.ToString()?.Trim(),
                        SensorCategory.Gpu));
                }
            }

            var adlxDll = Path.Combine(Environment.SystemDirectory, "amdadlx64.dll");
            var adlDll = Path.Combine(Environment.SystemDirectory, "atiadlxx.dll");
            _amdDriverDllPresent = File.Exists(adlxDll) || File.Exists(adlDll) ||
                                   NativeLibrary.TryLoad("amdadlx64.dll", out var h1) && FreeAndTrue(h1);

            IsAvailable = _hardware.Count > 0 && _amdDriverDllPresent;
            Health = IsAvailable ? ProviderHealth.Degraded : ProviderHealth.Degraded;
            HealthReason = _hardware.Count == 0
                ? "No AMD Radeon GPU detected on this system."
                : _amdDriverDllPresent
                    ? "AMD Radeon driver detected; native ADLX SWIG/COM bridge is not bundled (use LibreHardwareMonitor & Windows.GpuEngine fallback; ADLX hardware test pending)."
                    : "AMD GPU detected, but ADLX/ADL driver library was not found.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"AMD detection error: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    private static bool FreeAndTrue(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        _lastChecked = DateTimeOffset.UtcNow;
        if (_hardware.Count == 0)
            return Task.FromResult<IReadOnlyList<SensorReading>>(Array.Empty<SensorReading>());

        var reading = SensorReading.Unavailable(
            "amd.adlx.status",
            $"{_hardware[0].Name} ADLX Telemetry",
            SensorCategory.Gpu,
            _hardware[0].Id,
            _hardware[0].Name,
            Name,
            SensorAvailability.Unsupported,
            HealthReason ?? "Native ADLX wrapper not active; hardware testing on AMD GPU is pending.");

        return Task.FromResult<IReadOnlyList<SensorReading>>(new[] { reading });
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
