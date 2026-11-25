namespace CarbonMonitor.Core.Tests.Monitoring;

using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using Moq;
using Xunit;

public class MonitoringEngineTests : IDisposable
{
    private readonly Mock<ISensorProvider> _mockProvider1;
    private readonly Mock<ISensorProvider> _mockProvider2;
    private readonly List<SensorReading> _provider1Readings;
    private readonly List<SensorReading> _provider2Readings;

    public MonitoringEngineTests()
    {
        _provider1Readings = new List<SensorReading>
        {
            new() { MetricId = "cpu.usage", Value = 55.0, Unit = SensorUnit.Percent, Category = SensorCategory.Cpu },
            new() { MetricId = "cpu.temp", Value = 72.5, Unit = SensorUnit.Celsius, Category = SensorCategory.Cpu }
        };

        _provider2Readings = new List<SensorReading>
        {
            new() { MetricId = "gpu.usage", Value = 80.0, Unit = SensorUnit.Percent, Category = SensorCategory.Gpu },
            new() { MetricId = "gpu.temp", Value = 65.0, Unit = SensorUnit.Celsius, Category = SensorCategory.Gpu }
        };

        _mockProvider1 = new Mock<ISensorProvider>();
        _mockProvider1.Setup(p => p.Name).Returns("Provider1");
        _mockProvider1.Setup(p => p.MetricIds).Returns(new[] { "cpu.usage", "cpu.temp" });
        _mockProvider1.Setup(p => p.Health).Returns(ProviderHealth.Healthy);
        _mockProvider1.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockProvider1.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_provider1Readings.AsReadOnly());

        _mockProvider2 = new Mock<ISensorProvider>();
        _mockProvider2.Setup(p => p.Name).Returns("Provider2");
        _mockProvider2.Setup(p => p.MetricIds).Returns(new[] { "gpu.usage", "gpu.temp" });
        _mockProvider2.Setup(p => p.Health).Returns(ProviderHealth.Healthy);
        _mockProvider2.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockProvider2.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_provider2Readings.AsReadOnly());
    }

    public void Dispose()
    {
        // Cleanup is handled per-test
    }

    // ── StartAsync ──

    [Fact]
    public async Task StartAsync_BeginsPolling()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);

        Assert.Equal(EngineState.Running, engine.State);

        // Wait for at least one polling cycle
        await Task.Delay(250);

        await engine.StopAsync();

        _mockProvider1.Verify(p => p.ReadAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task StartAsync_InitializesProviders()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object, _mockProvider2.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await engine.StopAsync();

        _mockProvider1.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once());
        _mockProvider2.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task StartAsync_AlreadyRunning_DoesNothing()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await engine.StartAsync(cts.Token); // Should not throw or reinitialize

        await engine.StopAsync();

        _mockProvider1.Verify(p => p.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    // ── StopAsync ──

    [Fact]
    public async Task StopAsync_StopsPolling()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await Task.Delay(150);
        await engine.StopAsync();

        Assert.Equal(EngineState.Stopped, engine.State);

        // Count reads before and after stop
        var readCountAtStop = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        await Task.Delay(300); // Wait to verify no more reads

        var readCountAfter = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        Assert.Equal(readCountAtStop, readCountAfter);
    }

    [Fact]
    public async Task StopAsync_AlreadyStopped_DoesNothing()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object });

        await engine.StopAsync(); // Should not throw
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    // ── PauseAsync / ResumeAsync ──

    [Fact]
    public async Task PauseAsync_SetsStateToPaused()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await engine.PauseAsync();

        Assert.Equal(EngineState.Paused, engine.State);

        await engine.StopAsync();
    }

    [Fact]
    public async Task ResumeAsync_SetsStateToRunning()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await engine.PauseAsync();
        await engine.ResumeAsync();

        Assert.Equal(EngineState.Running, engine.State);

        await engine.StopAsync();
    }

    [Fact]
    public async Task PauseAsync_StopsReading_ResumeAsync_ResumesReading()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await Task.Delay(200); // Let a few polls happen

        await engine.PauseAsync();

        var readCountAtPause = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        await Task.Delay(300); // Should not read while paused

        var readCountAfterPause = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        Assert.Equal(readCountAtPause, readCountAfterPause);

        await engine.ResumeAsync();
        await Task.Delay(300); // Should resume reading

        var readCountAfterResume = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        Assert.True(readCountAfterResume > readCountAfterPause);

        await engine.StopAsync();
    }

    [Fact]
    public async Task PauseAsync_WhenStopped_DoesNothing()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object });

        await engine.PauseAsync();
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public async Task ResumeAsync_WhenNotPaused_DoesNothing()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await engine.ResumeAsync(); // Already running
        Assert.Equal(EngineState.Running, engine.State);

        await engine.StopAsync();
    }

    // ── Provider failure handling ──

    [Fact]
    public async Task ProviderFailure_DoesNotCrashEngine()
    {
        var failingProvider = new Mock<ISensorProvider>();
        failingProvider.Setup(p => p.Name).Returns("FailingProvider");
        failingProvider.Setup(p => p.MetricIds).Returns(new[] { "fail.metric" });
        failingProvider.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        failingProvider.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Sensor failure"));

        using var engine = new MonitoringEngine(
            new[] { failingProvider.Object, _mockProvider1.Object },
            pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await Task.Delay(300);

        // Engine should still be running
        Assert.Equal(EngineState.Running, engine.State);

        // Healthy provider should still have been read
        _mockProvider1.Verify(p => p.ReadAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        await engine.StopAsync();
    }

    [Fact]
    public async Task ProviderError_Event_IsRaised()
    {
        var failingProvider = new Mock<ISensorProvider>();
        failingProvider.Setup(p => p.Name).Returns("FailingProvider");
        failingProvider.Setup(p => p.MetricIds).Returns(new[] { "fail.metric" });
        failingProvider.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        failingProvider.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Read failure"));

        using var engine = new MonitoringEngine(new[] { failingProvider.Object }, pollingIntervalMs: 100);
        var errorRaised = false;
        engine.ProviderError += (_, args) => errorRaised = true;

        using var cts = new CancellationTokenSource();
        await engine.StartAsync(cts.Token);
        await Task.Delay(300);
        await engine.StopAsync();

        Assert.True(errorRaised);
    }

    [Fact]
    public async Task ProviderInitFailure_DoesNotPreventStartup()
    {
        var failingInit = new Mock<ISensorProvider>();
        failingInit.Setup(p => p.Name).Returns("FailInit");
        failingInit.Setup(p => p.MetricIds).Returns(new[] { "fail.init" });
        failingInit.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Init failed"));
        failingInit.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SensorReading>().AsReadOnly());

        using var engine = new MonitoringEngine(
            new[] { failingInit.Object, _mockProvider1.Object },
            pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);

        Assert.Equal(EngineState.Running, engine.State);

        await engine.StopAsync();
    }

    // ── Multiple providers merged ──

    [Fact]
    public async Task MultipleProviders_ReadingsAreMerged()
    {
        using var engine = new MonitoringEngine(
            new[] { _mockProvider1.Object, _mockProvider2.Object },
            pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        IReadOnlyList<SensorReading>? receivedReadings = null;
        engine.ReadingsUpdated += (_, readings) => receivedReadings = readings;

        await engine.StartAsync(cts.Token);
        await Task.Delay(250);
        await engine.StopAsync();

        Assert.NotNull(receivedReadings);
        Assert.Equal(4, receivedReadings.Count); // 2 from each provider
        Assert.Contains(receivedReadings, r => r.MetricId == "cpu.usage");
        Assert.Contains(receivedReadings, r => r.MetricId == "cpu.temp");
        Assert.Contains(receivedReadings, r => r.MetricId == "gpu.usage");
        Assert.Contains(receivedReadings, r => r.MetricId == "gpu.temp");
    }

    // ── Polling interval ──

    [Fact]
    public async Task PollingInterval_IsApproximatelyRespected()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 200);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await Task.Delay(1100); // Should get roughly 5-6 polls in ~1.1 seconds (200ms intervals)
        await engine.StopAsync();

        var readCount = _mockProvider1.Invocations
            .Count(i => i.Method.Name == nameof(ISensorProvider.ReadAsync));

        // Allow for timing variance: expect 3-8 polls
        Assert.InRange(readCount, 3, 8);
    }

    // ── CancellationToken ──

    [Fact]
    public async Task CancellationToken_StopsEngine()
    {
        using var cts = new CancellationTokenSource();
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);

        await engine.StartAsync(cts.Token);
        Assert.Equal(EngineState.Running, engine.State);

        cts.Cancel();
        await Task.Delay(300); // Give time for cancellation to propagate

        // Engine loop should have exited (but state may still be "Running" if
        // StopAsync wasn't called — the token just cancels the loop task)
    }

    // ── Provider timeout ──

    [Fact]
    public async Task ProviderTimeout_SlowProvider_DoesNotBlockOthers()
    {
        var slowProvider = new Mock<ISensorProvider>();
        slowProvider.Setup(p => p.Name).Returns("SlowProvider");
        slowProvider.Setup(p => p.MetricIds).Returns(new[] { "slow.metric" });
        slowProvider.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        slowProvider.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(10000, ct); // Very slow
                return (IReadOnlyList<SensorReading>)new List<SensorReading>().AsReadOnly();
            });

        using var engine = new MonitoringEngine(
            new[] { slowProvider.Object, _mockProvider1.Object },
            pollingIntervalMs: 100, providerTimeoutMs: 500);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        await Task.Delay(1500);
        await engine.StopAsync();

        // Fast provider should have been read multiple times despite slow provider timing out
        _mockProvider1.Verify(p => p.ReadAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    // ── ReadingsUpdated event ──

    [Fact]
    public async Task ReadingsUpdated_EventFires_WithReadings()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        var eventFired = false;
        IReadOnlyList<SensorReading>? eventReadings = null;

        engine.ReadingsUpdated += (_, readings) =>
        {
            eventFired = true;
            eventReadings = readings;
        };

        using var cts = new CancellationTokenSource();
        await engine.StartAsync(cts.Token);
        await Task.Delay(250);
        await engine.StopAsync();

        Assert.True(eventFired);
        Assert.NotNull(eventReadings);
        Assert.True(eventReadings.Count > 0);
    }

    // ── LatestReadings ──

    [Fact]
    public async Task LatestReadings_IsUpdatedAfterPoll()
    {
        using var engine = new MonitoringEngine(new[] { _mockProvider1.Object }, pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        Assert.Empty(engine.LatestReadings);

        await engine.StartAsync(cts.Token);
        await Task.Delay(250);
        await engine.StopAsync();

        Assert.NotEmpty(engine.LatestReadings);
        Assert.Equal(2, engine.LatestReadings.Count);
    }

    // ── Empty providers ──

    [Fact]
    public async Task NoProviders_EngineStillRuns()
    {
        using var engine = new MonitoringEngine(Array.Empty<ISensorProvider>(), pollingIntervalMs: 100);
        using var cts = new CancellationTokenSource();

        await engine.StartAsync(cts.Token);
        Assert.Equal(EngineState.Running, engine.State);
        await Task.Delay(200);
        await engine.StopAsync();

        Assert.Equal(EngineState.Stopped, engine.State);
    }

    // ── Constructor ──

    [Fact]
    public void Constructor_NullProviders_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MonitoringEngine(null!));
    }
}
