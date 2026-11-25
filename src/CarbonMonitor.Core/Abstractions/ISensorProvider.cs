namespace CarbonMonitor.Core.Abstractions;

using CarbonMonitor.Core.Models;

/// <summary>
/// Health status of a sensor provider.
/// </summary>
public enum ProviderHealth
{
    Healthy,
    Degraded,
    Faulted,
    NotInitialized
}

/// <summary>
/// Contract for a sensor data provider.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Provider name for identification.</summary>
    string Name { get; }

    /// <summary>Supported metric IDs this provider can supply.</summary>
    IReadOnlyList<string> MetricIds { get; }

    /// <summary>Current health status.</summary>
    ProviderHealth Health { get; }

    /// <summary>Reason for unhealthy state, if any.</summary>
    string? HealthReason { get; }

    /// <summary>Initialize the provider. Returns true if successful.</summary>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Read current sensor values.</summary>
    Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default);
}
