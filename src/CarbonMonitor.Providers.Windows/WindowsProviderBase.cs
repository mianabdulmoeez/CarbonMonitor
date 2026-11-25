namespace CarbonMonitor.Providers.Windows;

using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

/// <summary>
/// Base class for Windows OS-counter and WMI-based sensor providers.
/// </summary>
public abstract class WindowsProviderBase : IHardwareProvider
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyList<string> MetricIds { get; }
    public ProviderHealth Health { get; protected set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; protected set; }
    public bool IsAvailable { get; protected set; }

    protected readonly List<HardwareInfo> DetectedHardware = new();
    protected DateTimeOffset LastChecked = DateTimeOffset.MinValue;
    protected int LastSensorCount;

    public virtual Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Health = ProviderHealth.Healthy;
        IsAvailable = true;
        LastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(true);
    }

    public abstract Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default);

    public virtual IReadOnlyList<HardwareInfo> GetDetectedHardware() => DetectedHardware.AsReadOnly();

    public virtual ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        LastChecked == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : LastChecked,
        LastSensorCount,
        IsAvailable);

    public virtual void Dispose() { }
}
