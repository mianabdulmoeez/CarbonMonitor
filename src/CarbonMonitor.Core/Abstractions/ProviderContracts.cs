using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Core.Abstractions;

/// <summary>
/// Extended contract for hardware-discovering sensor providers.
/// </summary>
public interface IHardwareProvider : ISensorProvider
{
    /// <summary>Human-readable summary of what this provider monitors.</summary>
    string Description { get; }

    /// <summary>Whether the provider detected compatible hardware or OS counters.</summary>
    bool IsAvailable { get; }

    /// <summary>Hardware components discovered by this provider.</summary>
    IReadOnlyList<HardwareInfo> GetDetectedHardware();

    /// <summary>Detailed provider health snapshot.</summary>
    ProviderHealthInfo GetHealthInfo();
}

/// <summary>
/// Capability-aware GPU telemetry provider.
/// </summary>
public interface IGpuTelemetryProvider : IHardwareProvider
{
    bool SupportsTemperature { get; }
    bool SupportsClock { get; }
    bool SupportsMemoryUsage { get; }
    bool SupportsFanSpeed { get; }
    string GpuType { get; }
}

/// <summary>
/// Capability-aware storage telemetry provider.
/// </summary>
public interface IStorageTelemetryProvider : IHardwareProvider
{
    bool SupportsTemperature { get; }
    bool SupportsSmartHealth { get; }
}

/// <summary>
/// Network throughput and probe provider.
/// </summary>
public interface INetworkTelemetryProvider : IHardwareProvider
{
    Task SetPingTargetAsync(string host, int intervalSeconds = 5, CancellationToken cancellationToken = default);
}

/// <summary>
/// Game process and optional frame-time telemetry provider.
/// </summary>
public interface IGameTelemetryProvider : IHardwareProvider
{
    bool SupportsFps { get; }
    bool SupportsFrameTime { get; }
    Task<bool> IsGameRunningAsync(string processName, CancellationToken cancellationToken = default);
    void SetTargetProcess(string processName, uint pid);
    void ClearTarget();
}
