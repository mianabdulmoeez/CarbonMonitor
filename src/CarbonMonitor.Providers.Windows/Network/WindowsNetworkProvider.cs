using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Providers.Windows.Network;

/// <summary>
/// Measures network adapter throughput and rate-limited ICMP probe latency/loss.
/// Explicitly labels packet loss as generic network probe loss, not game-server packet loss.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkProvider : WindowsProviderBase, INetworkTelemetryProvider
{
    private readonly Queue<bool> _pingWindow = new();
    private const int PingWindowSize = 20;

    private string _pingTarget = "8.8.8.8";
    private TimeSpan _pingInterval = TimeSpan.FromSeconds(5);
    private DateTimeOffset _lastPingAt = DateTimeOffset.MinValue;
    private double? _lastLatencyMs;
    private double? _lastPacketLossPct;
    private string? _pingError;

    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTimeOffset _prevBytesSampleAt = DateTimeOffset.MinValue;
    private string _activeAdapterName = "Network Interface";

    public override string Name => "Windows.Network";
    public override string Description => "Adapter throughput and rate-limited ICMP network probe latency/loss";
    public override IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "network.download.speed",
        "network.upload.speed",
        "network.ping.latency",
        "network.ping.packetloss"
    };

    public Task SetPingTargetAsync(string host, int intervalSeconds = 5, CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(host) ? "8.8.8.8" : host.Trim();
        if (!string.Equals(_pingTarget, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            _pingTarget = trimmed;
            _pingWindow.Clear();
            _lastLatencyMs = null;
            _lastPacketLossPct = null;
            _lastPingAt = DateTimeOffset.MinValue;
        }
        _pingInterval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 2, 60));
        return Task.CompletedTask;
    }

    public override Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DetectedHardware.Clear();
            var (rx, tx, name) = SampleNetworkTotals();
            _prevBytesReceived = rx;
            _prevBytesSent = tx;
            _prevBytesSampleAt = DateTimeOffset.UtcNow;
            _activeAdapterName = name;

            IsAvailable = true;
            Health = ProviderHealth.Healthy;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = ex.Message;
        }

        LastChecked = DateTimeOffset.UtcNow;
        return Task.FromResult(IsAvailable);
    }

    public override async Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>(4);
        var now = DateTimeOffset.UtcNow;
        LastChecked = now;

        // 1. Adapter throughput
        try
        {
            var (rx, tx, name) = SampleNetworkTotals();
            _activeAdapterName = name;
            double elapsedSec = (now - _prevBytesSampleAt).TotalSeconds;
            if (elapsedSec > 0.2 && _prevBytesSampleAt != DateTimeOffset.MinValue)
            {
                double downMbps = Math.Max(0, (rx - _prevBytesReceived) / elapsedSec) / (1024.0 * 1024.0);
                double upMbps = Math.Max(0, (tx - _prevBytesSent) / elapsedSec) / (1024.0 * 1024.0);

                readings.Add(new SensorReading
                {
                    MetricId = "network.download.speed",
                    DisplayName = "Download Speed",
                    Value = Math.Round(downMbps, 2),
                    Unit = SensorUnit.MBPerSecond,
                    Category = SensorCategory.Network,
                    HardwareId = "net0",
                    HardwareName = _activeAdapterName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
                readings.Add(new SensorReading
                {
                    MetricId = "network.upload.speed",
                    DisplayName = "Upload Speed",
                    Value = Math.Round(upMbps, 2),
                    Unit = SensorUnit.MBPerSecond,
                    Category = SensorCategory.Network,
                    HardwareId = "net0",
                    HardwareName = _activeAdapterName,
                    ProviderName = Name,
                    Timestamp = now,
                    Availability = SensorAvailability.Available,
                    SourceType = SensorSourceType.OsCounter
                });
            }

            _prevBytesReceived = rx;
            _prevBytesSent = tx;
            _prevBytesSampleAt = now;
        }
        catch (Exception ex)
        {
            readings.Add(SensorReading.Unavailable(
                "network.download.speed", "Download Speed", SensorCategory.Network,
                "net0", _activeAdapterName, Name, SensorAvailability.TemporarilyUnavailable, ex.Message));
        }

        // 2. Rate-limited ICMP probe
        if (now - _lastPingAt >= _pingInterval)
        {
            _lastPingAt = now;
            await RunPingProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        var probeNote = $"ICMP probe to {_pingTarget}. Measures generic network reachability, NOT game-server packet loss.";
        if (_lastLatencyMs is double latency)
        {
            readings.Add(new SensorReading
            {
                MetricId = "network.ping.latency",
                DisplayName = $"Ping ({_pingTarget})",
                Value = Math.Round(latency, 1),
                Unit = SensorUnit.Milliseconds,
                Category = SensorCategory.Network,
                HardwareId = "net.probe",
                HardwareName = $"ICMP Probe ({_pingTarget})",
                ProviderName = Name,
                Timestamp = _lastPingAt,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Measured,
                QualityNote = probeNote
            });
        }
        else
        {
            readings.Add(SensorReading.Unavailable(
                "network.ping.latency", $"Ping ({_pingTarget})", SensorCategory.Network,
                "net.probe", $"ICMP Probe ({_pingTarget})", Name,
                SensorAvailability.TemporarilyUnavailable,
                _pingError ?? "No ICMP reply received", probeNote));
        }

        if (_lastPacketLossPct is double loss)
        {
            readings.Add(new SensorReading
            {
                MetricId = "network.ping.packetloss",
                DisplayName = "Network Probe Packet Loss",
                Value = Math.Round(loss, 1),
                Unit = SensorUnit.Percent,
                Category = SensorCategory.Network,
                HardwareId = "net.probe",
                HardwareName = $"ICMP Probe ({_pingTarget})",
                ProviderName = Name,
                Timestamp = _lastPingAt,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Measured,
                QualityNote = probeNote
            });
        }

        LastSensorCount = readings.Count(r => r.Availability == SensorAvailability.Available);
        return readings.AsReadOnly();
    }

    private async Task RunPingProbeAsync(CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_pingTarget, 1500).WaitAsync(ct).ConfigureAwait(false);
            bool success = reply.Status == IPStatus.Success;
            RecordPingSample(success);
            if (success)
            {
                _lastLatencyMs = reply.RoundtripTime;
                _pingError = null;
            }
            else
            {
                _pingError = $"ICMP status: {reply.Status}";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RecordPingSample(false);
            _pingError = ex.Message;
        }
    }

    private void RecordPingSample(bool success)
    {
        _pingWindow.Enqueue(success);
        while (_pingWindow.Count > PingWindowSize)
            _pingWindow.Dequeue();

        int failed = _pingWindow.Count(s => !s);
        _lastPacketLossPct = _pingWindow.Count > 0 ? failed * 100.0 / _pingWindow.Count : 0;
    }

    private (long rx, long tx, string primaryName) SampleNetworkTotals()
    {
        long rx = 0, tx = 0;
        string primary = "Network Interface";
        bool foundPrimary = false;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            if (!foundPrimary)
            {
                primary = nic.Description;
                foundPrimary = true;
                if (DetectedHardware.Count == 0)
                {
                    DetectedHardware.Add(new HardwareInfo(nic.Id, nic.Description, nic.Name, null, SensorCategory.Network));
                }
            }

            var stats = nic.GetIPStatistics();
            rx += stats.BytesReceived;
            tx += stats.BytesSent;
        }

        return (rx, tx, primary);
    }
}
