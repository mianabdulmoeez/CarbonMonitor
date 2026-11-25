using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Hardware.Nvidia;

/// <summary>
/// NVIDIA GPU provider dynamically loading driver-installed nvml.dll via NativeLibrary.
/// Never bundles proprietary NVIDIA binaries. Degrades gracefully when no NVIDIA driver is present.
/// NOTE: Implemented against documented NVML C API; requires validation on physical NVIDIA hardware (e.g., RTX 3060).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NvmlProvider : IGpuTelemetryProvider
{
    private const int NvmlSuccess = 0;
    private const int NvmlTemperatureGpu = 0;
    private const int NvmlClockGraphics = 0;
    private const int NvmlClockMem = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    private delegate int NvmlInitDelegate();
    private delegate int NvmlShutdownDelegate();
    private delegate int NvmlDeviceGetCountDelegate(ref uint deviceCount);
    private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, ref IntPtr device);
    private delegate int NvmlDeviceGetNameDelegate(IntPtr device, byte[] name, uint length);
    private delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, int sensorType, ref uint temp);
    private delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, ref NvmlUtilization utilization);
    private delegate int NvmlDeviceGetClockInfoDelegate(IntPtr device, int clockType, ref uint clock);
    private delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, ref NvmlMemory memory);
    private delegate int NvmlDeviceGetFanSpeedDelegate(IntPtr device, ref uint speed);
    private delegate int NvmlDeviceGetPowerUsageDelegate(IntPtr device, ref uint powerMilliwatts);

    private readonly List<HardwareInfo> _hardware = new();
    private readonly List<IntPtr> _deviceHandles = new();
    private IntPtr _nvmlLib;
    private bool _initialized;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;
    private int _lastSensorCount;

    private NvmlInitDelegate? _nvmlInit;
    private NvmlShutdownDelegate? _nvmlShutdown;
    private NvmlDeviceGetCountDelegate? _nvmlGetCount;
    private NvmlDeviceGetHandleByIndexDelegate? _nvmlGetHandle;
    private NvmlDeviceGetNameDelegate? _nvmlGetName;
    private NvmlDeviceGetTemperatureDelegate? _nvmlGetTemp;
    private NvmlDeviceGetUtilizationRatesDelegate? _nvmlGetUtil;
    private NvmlDeviceGetClockInfoDelegate? _nvmlGetClock;
    private NvmlDeviceGetMemoryInfoDelegate? _nvmlGetMemory;
    private NvmlDeviceGetFanSpeedDelegate? _nvmlGetFan;
    private NvmlDeviceGetPowerUsageDelegate? _nvmlGetPower;

    public string Name => "Hardware.Nvidia.NVML";
    public string Description => "NVIDIA GPU telemetry via driver-installed nvml.dll (untested without NVIDIA hardware)";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "nvidia.gpu0.temperature",
        "nvidia.gpu0.usage",
        "nvidia.gpu0.clock.graphics",
        "nvidia.gpu0.clock.memory",
        "nvidia.gpu0.vram.used",
        "nvidia.gpu0.vram.total",
        "nvidia.gpu0.fan.speed",
        "nvidia.gpu0.power"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }

    public bool SupportsTemperature { get; private set; }
    public bool SupportsClock { get; private set; }
    public bool SupportsMemoryUsage { get; private set; }
    public bool SupportsFanSpeed { get; private set; }
    public string GpuType => "Dedicated";

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Degraded;
            HealthReason = "NVIDIA NVML provider requires Windows.";
            return Task.FromResult(false);
        }

        try
        {
            var candidatePaths = new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
                "nvml.dll"
            };

            foreach (var path in candidatePaths)
            {
                if (NativeLibrary.TryLoad(path, out _nvmlLib))
                    break;
            }

            if (_nvmlLib == IntPtr.Zero)
            {
                IsAvailable = false;
                Health = ProviderHealth.Degraded;
                HealthReason = "nvml.dll not found on system (NVIDIA display driver not installed).";
                _lastChecked = DateTimeOffset.UtcNow;
                return Task.FromResult(false);
            }

            LoadFunctions();

            if (_nvmlInit is null || _nvmlInit() != NvmlSuccess)
            {
                IsAvailable = false;
                Health = ProviderHealth.Degraded;
                HealthReason = "nvmlInit_v2 failed.";
                _lastChecked = DateTimeOffset.UtcNow;
                return Task.FromResult(false);
            }

            _initialized = true;
            uint count = 0;
            _nvmlGetCount?.Invoke(ref count);

            _hardware.Clear();
            _deviceHandles.Clear();
            for (uint i = 0; i < count; i++)
            {
                IntPtr handle = IntPtr.Zero;
                if (_nvmlGetHandle?.Invoke(i, ref handle) == NvmlSuccess && handle != IntPtr.Zero)
                {
                    _deviceHandles.Add(handle);
                    var nameBytes = new byte[96];
                    _nvmlGetName?.Invoke(handle, nameBytes, (uint)nameBytes.Length);
                    var gpuName = Encoding.UTF8.GetString(nameBytes).Split('\0')[0].Trim();
                    if (string.IsNullOrEmpty(gpuName)) gpuName = $"NVIDIA GPU {i}";
                    _hardware.Add(new HardwareInfo($"nvidia.gpu{i}", gpuName, "NVIDIA", null, SensorCategory.Gpu));
                }
            }

            IsAvailable = _deviceHandles.Count > 0;
            SupportsTemperature = IsAvailable;
            SupportsClock = IsAvailable;
            SupportsMemoryUsage = IsAvailable;
            SupportsFanSpeed = IsAvailable;
            Health = IsAvailable ? ProviderHealth.Healthy : ProviderHealth.Degraded;
            HealthReason = IsAvailable ? null : "NVML initialized, but no NVIDIA GPU devices were enumerated.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = $"NVML init error: {ex.Message}";
        }

        _lastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    private void LoadFunctions()
    {
        T? GetExport<T>(string name) where T : Delegate
        {
            if (NativeLibrary.TryGetExport(_nvmlLib, name, out var addr))
                return Marshal.GetDelegateForFunctionPointer<T>(addr);
            return null;
        }

        _nvmlInit = GetExport<NvmlInitDelegate>("nvmlInit_v2");
        _nvmlShutdown = GetExport<NvmlShutdownDelegate>("nvmlShutdown");
        _nvmlGetCount = GetExport<NvmlDeviceGetCountDelegate>("nvmlDeviceGetCount_v2");
        _nvmlGetHandle = GetExport<NvmlDeviceGetHandleByIndexDelegate>("nvmlDeviceGetHandleByIndex_v2");
        _nvmlGetName = GetExport<NvmlDeviceGetNameDelegate>("nvmlDeviceGetName");
        _nvmlGetTemp = GetExport<NvmlDeviceGetTemperatureDelegate>("nvmlDeviceGetTemperature");
        _nvmlGetUtil = GetExport<NvmlDeviceGetUtilizationRatesDelegate>("nvmlDeviceGetUtilizationRates");
        _nvmlGetClock = GetExport<NvmlDeviceGetClockInfoDelegate>("nvmlDeviceGetClockInfo");
        _nvmlGetMemory = GetExport<NvmlDeviceGetMemoryInfoDelegate>("nvmlDeviceGetMemoryInfo");
        _nvmlGetFan = GetExport<NvmlDeviceGetFanSpeedDelegate>("nvmlDeviceGetFanSpeed");
        _nvmlGetPower = GetExport<NvmlDeviceGetPowerUsageDelegate>("nvmlDeviceGetPowerUsage");
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        _lastChecked = now;

        if (!IsAvailable)
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());

        const string note = "NVIDIA NVML driver-hosted query (requires validation on physical NVIDIA GPU).";

        for (int i = 0; i < _deviceHandles.Count; i++)
        {
            var handle = _deviceHandles[i];
            var hwId = $"nvidia.gpu{i}";
            var hwName = i < _hardware.Count ? _hardware[i].Name : $"NVIDIA GPU {i}";

            uint temp = 0;
            if (_nvmlGetTemp?.Invoke(handle, NvmlTemperatureGpu, ref temp) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = i == 0 ? "gpu.temperature" : $"nvidia.gpu{i}.temperature",
                    DisplayName = $"{hwName} Temp",
                    Value = temp,
                    Unit = SensorUnit.Celsius,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            var util = new NvmlUtilization();
            if (_nvmlGetUtil?.Invoke(handle, ref util) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.usage",
                    DisplayName = $"{hwName} Utilization",
                    Value = util.Gpu,
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            uint graphicsClock = 0;
            if (_nvmlGetClock?.Invoke(handle, NvmlClockGraphics, ref graphicsClock) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = i == 0 ? "gpu.clock" : $"nvidia.gpu{i}.clock.graphics",
                    DisplayName = $"{hwName} Graphics Clock",
                    Value = graphicsClock,
                    Unit = SensorUnit.MHz,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            uint memClock = 0;
            if (_nvmlGetClock?.Invoke(handle, NvmlClockMem, ref memClock) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.clock.memory",
                    DisplayName = $"{hwName} Memory Clock",
                    Value = memClock,
                    Unit = SensorUnit.MHz,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            var mem = new NvmlMemory();
            if (_nvmlGetMemory?.Invoke(handle, ref mem) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.vram.used",
                    DisplayName = $"{hwName} Dedicated VRAM Used",
                    Value = Math.Round(mem.Used / (1024.0 * 1024.0), 1),
                    Unit = SensorUnit.MB,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.vram.total",
                    DisplayName = $"{hwName} Dedicated VRAM Total",
                    Value = Math.Round(mem.Total / (1024.0 * 1024.0), 1),
                    Unit = SensorUnit.MB,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            uint fan = 0;
            if (_nvmlGetFan?.Invoke(handle, ref fan) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.fan.speed",
                    DisplayName = $"{hwName} Fan Speed",
                    Value = fan,
                    Unit = SensorUnit.Percent,
                    Category = SensorCategory.Cooling,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }

            uint powerMw = 0;
            if (_nvmlGetPower?.Invoke(handle, ref powerMw) == NvmlSuccess)
            {
                readings.Add(new SensorReading
                {
                    MetricId = $"nvidia.gpu{i}.power",
                    DisplayName = $"{hwName} Power",
                    Value = Math.Round(powerMw / 1000.0, 1),
                    Unit = SensorUnit.Watts,
                    Category = SensorCategory.Gpu,
                    HardwareId = hwId,
                    HardwareName = hwName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.Measured,
                    QualityNote = note
                });
            }
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

    public void Dispose()
    {
        if (_initialized)
        {
            try { _nvmlShutdown?.Invoke(); } catch { }
            _initialized = false;
        }
        if (_nvmlLib != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_nvmlLib); } catch { }
            _nvmlLib = IntPtr.Zero;
        }
        _deviceHandles.Clear();
    }
}
