using CarbonMonitor.Core.Abstractions;

namespace CarbonMonitor.Core.Models;

/// <summary>
/// Rich health snapshot for a sensor provider.
/// </summary>
public sealed record ProviderHealthInfo(
    string ProviderName,
    ProviderHealth Status,
    string? ErrorMessage,
    DateTimeOffset LastChecked,
    int SensorCount,
    bool IsAvailable)
{
    public bool IsHealthy => Status == ProviderHealth.Healthy;
}
