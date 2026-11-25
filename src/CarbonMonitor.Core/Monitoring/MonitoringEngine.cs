namespace CarbonMonitor.Core.Monitoring;

using System.Collections.Concurrent;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;

/// <summary>
/// Engine state.
/// </summary>
public enum EngineState
{
    Stopped,
    Running,
    Paused
}

/// <summary>
/// Core monitoring engine that orchestrates capability-driven sensor providers.
/// </summary>
public class MonitoringEngine : IDisposable
{
    private readonly IReadOnlyList<ISensorProvider> _providers;
    private int _pollingIntervalMs;
    private readonly int _providerTimeoutMs;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private EngineState _state = EngineState.Stopped;
    private readonly SemaphoreSlim _pauseLock = new(1, 1);
    private volatile bool _isPaused;
    private volatile List<SensorReading> _latestReadings = new();
    private readonly ConcurrentDictionary<string, ProviderHealthInfo> _healthSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSuccessByMetric = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Current engine state.</summary>
    public EngineState State => _state;

    /// <summary>Configured monitoring mode (Always, GameOnly, OverlayOnly).</summary>
    public MonitoringMode Mode { get; set; } = MonitoringMode.Always;

    /// <summary>Current effective polling interval in milliseconds.</summary>
    public int PollingIntervalMs
    {
        get => _pollingIntervalMs;
        set => _pollingIntervalMs = Math.Clamp(value, 100, 60000);
    }

    /// <summary>Optional predicate evaluated each cycle to decide whether active polling should run.</summary>
    public Func<bool>? ShouldPollPredicate { get; set; }

    /// <summary>Latest merged readings from all providers.</summary>
    public IReadOnlyList<SensorReading> LatestReadings => _latestReadings.AsReadOnly();

    /// <summary>Event raised when new readings are available.</summary>
    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    /// <summary>Event raised when a provider encounters an error.</summary>
    public event EventHandler<(ISensorProvider Provider, Exception Error)>? ProviderError;

    public MonitoringEngine(IReadOnlyList<ISensorProvider> providers,
        int pollingIntervalMs = 2000, int providerTimeoutMs = 5000)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _pollingIntervalMs = pollingIntervalMs;
        _providerTimeoutMs = providerTimeoutMs;
    }

    /// <summary>Start the monitoring engine and initialize all providers.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state == EngineState.Running)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isPaused = false;

        foreach (var provider in _providers)
        {
            try
            {
                using var initCts = new CancellationTokenSource(_providerTimeoutMs);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, initCts.Token);
                var ok = await provider.InitializeAsync(linked.Token).ConfigureAwait(false);
                UpdateProviderSnapshot(provider, 0, ok ? null : provider.HealthReason);
            }
            catch (Exception ex)
            {
                UpdateProviderSnapshot(provider, 0, ex.Message);
                ProviderError?.Invoke(this, (provider, ex));
            }
        }

        _state = EngineState.Running;
        _pollingTask = PollAsync(_cts.Token);
    }

    /// <summary>Stop the monitoring engine.</summary>
    public async Task StopAsync()
    {
        if (_state == EngineState.Stopped)
            return;

        _cts?.Cancel();

        if (_pollingTask != null)
        {
            try
            {
                await _pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _state = EngineState.Stopped;
    }

    /// <summary>Pause monitoring (keeps engine alive but stops polling).</summary>
    public Task PauseAsync()
    {
        if (_state != EngineState.Running)
            return Task.CompletedTask;

        _isPaused = true;
        _state = EngineState.Paused;
        return Task.CompletedTask;
    }

    /// <summary>Resume monitoring after pause.</summary>
    public Task ResumeAsync()
    {
        if (_state != EngineState.Paused)
            return Task.CompletedTask;

        _isPaused = false;
        _state = EngineState.Running;
        return Task.CompletedTask;
    }

    /// <summary>Returns current health diagnostics for all registered providers.</summary>
    public IReadOnlyList<ProviderHealthInfo> GetProviderHealthInfo()
    {
        var list = new List<ProviderHealthInfo>(_providers.Count);
        foreach (var provider in _providers)
        {
            if (provider is IHardwareProvider hwProvider)
            {
                list.Add(hwProvider.GetHealthInfo());
            }
            else if (_healthSnapshots.TryGetValue(provider.Name, out var snap))
            {
                list.Add(snap with
                {
                    Status = provider.Health,
                    ErrorMessage = provider.HealthReason ?? snap.ErrorMessage
                });
            }
            else
            {
                list.Add(new ProviderHealthInfo(
                    provider.Name,
                    provider.Health,
                    provider.HealthReason,
                    DateTimeOffset.UtcNow,
                    0,
                    provider.Health == ProviderHealth.Healthy));
            }
        }
        return list.AsReadOnly();
    }

    /// <summary>Returns all hardware discovered across hardware-capable providers.</summary>
    public IReadOnlyList<HardwareInfo> GetAllDetectedHardware()
    {
        var hardware = new List<HardwareInfo>();
        foreach (var provider in _providers)
        {
            if (provider is IHardwareProvider hwProvider)
            {
                hardware.AddRange(hwProvider.GetDetectedHardware());
            }
        }
        return hardware.AsReadOnly();
    }

    /// <summary>Returns deduplicated physical GPU snapshots merged across active providers.</summary>
    public IReadOnlyList<PhysicalGpuSnapshot> GetAggregatedGpus()
        => GpuReadingAggregator.Aggregate(LatestReadings, GetAllDetectedHardware());

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool shouldPoll = !_isPaused && (ShouldPollPredicate?.Invoke() ?? true);

            if (shouldPoll)
            {
                var allReadings = new List<SensorReading>();

                foreach (var provider in _providers)
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(_providerTimeoutMs);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, timeoutCts.Token);

                        var readings = await provider.ReadAsync(linked.Token).ConfigureAwait(false);
                        foreach (var reading in readings)
                        {
                            if (reading.Availability == SensorAvailability.Available && (reading.Value.HasValue || !string.IsNullOrEmpty(reading.TextValue)))
                            {
                                _lastSuccessByMetric[reading.MetricId] = reading.Timestamp;
                                reading.LastSuccessfulUpdate = reading.Timestamp;
                            }
                            else if (_lastSuccessByMetric.TryGetValue(reading.MetricId, out var prevOk))
                            {
                                reading.LastSuccessfulUpdate = prevOk;
                            }
                        }
                        allReadings.AddRange(readings);
                        UpdateProviderSnapshot(provider, readings.Count(r => r.Availability == SensorAvailability.Available), provider.HealthReason);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var timeoutEx = new TimeoutException($"Provider {provider.Name} timed out");
                        UpdateProviderSnapshot(provider, 0, timeoutEx.Message);
                        ProviderError?.Invoke(this, (provider, timeoutEx));
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        UpdateProviderSnapshot(provider, 0, ex.Message);
                        ProviderError?.Invoke(this, (provider, ex));
                    }
                }

                _latestReadings = allReadings;
                ReadingsUpdated?.Invoke(this, allReadings.AsReadOnly());
            }

            try
            {
                await Task.Delay(_pollingIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateProviderSnapshot(ISensorProvider provider, int sensorCount, string? error)
    {
        bool available = provider is IHardwareProvider hw ? hw.IsAvailable : provider.Health == ProviderHealth.Healthy;
        _healthSnapshots[provider.Name] = new ProviderHealthInfo(
            provider.Name,
            provider.Health,
            error,
            DateTimeOffset.UtcNow,
            sensorCount,
            available);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _pauseLock.Dispose();
        foreach (var provider in _providers)
        {
            try { provider.Dispose(); } catch { }
        }
    }
}
