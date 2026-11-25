namespace CarbonMonitor.Providers.Tests;

using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using Moq;
using Xunit;

/// <summary>
/// A reusable test mock provider implementing ISensorProvider.
/// </summary>
public class TestMockProvider : ISensorProvider
{
    private readonly List<SensorReading> _readings;
    private bool _initialized;
    private bool _failOnInit;
    private bool _failOnRead;

    public string Name { get; }
    public IReadOnlyList<string> MetricIds { get; }
    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }

    public TestMockProvider(string name, IEnumerable<string> metricIds,
        IEnumerable<SensorReading>? readings = null,
        bool failOnInit = false, bool failOnRead = false)
    {
        Name = name;
        MetricIds = metricIds.ToList().AsReadOnly();
        _readings = readings?.ToList() ?? new List<SensorReading>();
        _failOnInit = failOnInit;
        _failOnRead = failOnRead;
    }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_failOnInit)
        {
            Health = ProviderHealth.Faulted;
            HealthReason = "Initialization failure (simulated)";
            throw new InvalidOperationException("Simulated init failure");
        }

        _initialized = true;
        Health = ProviderHealth.Healthy;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_failOnRead)
        {
            Health = ProviderHealth.Degraded;
            HealthReason = "Read failure (simulated)";
            throw new InvalidOperationException("Simulated read failure");
        }

        // Update timestamps
        foreach (var reading in _readings)
        {
            reading.Timestamp = DateTimeOffset.UtcNow;
        }

        return Task.FromResult<IReadOnlyList<SensorReading>>(_readings.AsReadOnly());
    }

    public void AddReading(SensorReading reading) => _readings.Add(reading);

    public bool IsInitialized => _initialized;

    public void Dispose() { }
}

public class MockSensorProviderTests
{
    // ── Provider returns proper MetricIds ──

    [Fact]
    public void Provider_Returns_ProperMetricIds()
    {
        var provider = new TestMockProvider("TestCpu",
            new[] { "cpu.usage", "cpu.temperature", "cpu.clock" });

        Assert.Equal(3, provider.MetricIds.Count);
        Assert.Contains("cpu.usage", provider.MetricIds);
        Assert.Contains("cpu.temperature", provider.MetricIds);
        Assert.Contains("cpu.clock", provider.MetricIds);
    }

    [Fact]
    public void Provider_MetricIds_AreStable()
    {
        var provider = new TestMockProvider("TestGpu",
            new[] { "gpu.usage", "gpu.temp" });

        var first = provider.MetricIds;
        var second = provider.MetricIds;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Provider_EmptyMetricIds_IsValid()
    {
        var provider = new TestMockProvider("Empty", Array.Empty<string>());
        Assert.Empty(provider.MetricIds);
    }

    // ── Provider handles initialization failure gracefully ──

    [Fact]
    public async Task Provider_InitializationFailure_SetsHealthToFaulted()
    {
        var provider = new TestMockProvider("FailInit",
            new[] { "test.metric" }, failOnInit: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeAsync());

        Assert.Equal(ProviderHealth.Faulted, provider.Health);
        Assert.NotNull(provider.HealthReason);
    }

    [Fact]
    public async Task Provider_SuccessfulInit_SetsHealthToHealthy()
    {
        var provider = new TestMockProvider("GoodInit",
            new[] { "test.metric" });

        var result = await provider.InitializeAsync();

        Assert.True(result);
        Assert.Equal(ProviderHealth.Healthy, provider.Health);
        Assert.True(provider.IsInitialized);
    }

    // ── Provider health reporting ──

    [Fact]
    public void Provider_InitialHealth_IsNotInitialized()
    {
        var provider = new TestMockProvider("New", new[] { "test" });
        Assert.Equal(ProviderHealth.NotInitialized, provider.Health);
        Assert.Null(provider.HealthReason);
    }

    [Fact]
    public async Task Provider_ReadFailure_SetsHealthToDegraded()
    {
        var provider = new TestMockProvider("FailRead",
            new[] { "test.metric" }, failOnRead: true);

        await provider.InitializeAsync();
        Assert.Equal(ProviderHealth.Healthy, provider.Health);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ReadAsync());

        Assert.Equal(ProviderHealth.Degraded, provider.Health);
        Assert.Contains("Read failure", provider.HealthReason);
    }

    // ── Provider Read returns timestamped readings ──

    [Fact]
    public async Task Provider_Read_ReturnsTimestampedReadings()
    {
        var readings = new[]
        {
            new SensorReading
            {
                MetricId = "cpu.usage",
                Value = 50.0,
                Unit = SensorUnit.Percent,
                Category = SensorCategory.Cpu
            }
        };

        var provider = new TestMockProvider("TimestampTest",
            new[] { "cpu.usage" }, readings);

        await provider.InitializeAsync();
        var before = DateTimeOffset.UtcNow;
        var result = await provider.ReadAsync();
        var after = DateTimeOffset.UtcNow;

        Assert.Single(result);
        Assert.InRange(result[0].Timestamp, before, after);
    }

    [Fact]
    public async Task Provider_Read_UpdatesTimestampsOnEachRead()
    {
        var readings = new[]
        {
            new SensorReading { MetricId = "test", Value = 1.0 }
        };

        var provider = new TestMockProvider("TimeTest",
            new[] { "test" }, readings);

        await provider.InitializeAsync();

        var firstResult = await provider.ReadAsync();
        var firstTimestamp = firstResult[0].Timestamp;

        await Task.Delay(50);

        var secondResult = await provider.ReadAsync();
        var secondTimestamp = secondResult[0].Timestamp;

        Assert.True(secondTimestamp >= firstTimestamp);
    }

    // ── Multiple readings don't duplicate MetricIds ──

    [Fact]
    public async Task Provider_MultipleReadings_NoDuplicateMetricIds()
    {
        var readings = new[]
        {
            new SensorReading { MetricId = "cpu.usage", Value = 50.0 },
            new SensorReading { MetricId = "cpu.temp", Value = 65.0 },
            new SensorReading { MetricId = "cpu.clock", Value = 3600.0 }
        };

        var provider = new TestMockProvider("NoDupes",
            new[] { "cpu.usage", "cpu.temp", "cpu.clock" }, readings);

        await provider.InitializeAsync();
        var result = await provider.ReadAsync();

        var metricIds = result.Select(r => r.MetricId).ToList();
        Assert.Equal(metricIds.Distinct().Count(), metricIds.Count);
    }

    [Fact]
    public async Task Provider_ConsecutiveReads_ReturnConsistentMetricIds()
    {
        var readings = new[]
        {
            new SensorReading { MetricId = "mem.usage", Value = 70.0 },
            new SensorReading { MetricId = "mem.total", Value = 16384.0 }
        };

        var provider = new TestMockProvider("Consistent",
            new[] { "mem.usage", "mem.total" }, readings);

        await provider.InitializeAsync();

        var firstRead = await provider.ReadAsync();
        var secondRead = await provider.ReadAsync();

        var firstIds = firstRead.Select(r => r.MetricId).OrderBy(id => id).ToList();
        var secondIds = secondRead.Select(r => r.MetricId).OrderBy(id => id).ToList();

        Assert.Equal(firstIds, secondIds);
    }

    // ── Moq-based interface tests ──

    [Fact]
    public async Task MockProvider_ReturnsConfiguredReadings()
    {
        var mock = new Mock<ISensorProvider>();
        mock.Setup(p => p.Name).Returns("MockedProvider");
        mock.Setup(p => p.MetricIds).Returns(new[] { "disk.read", "disk.write" });
        mock.Setup(p => p.Health).Returns(ProviderHealth.Healthy);
        mock.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SensorReading>
            {
                new() { MetricId = "disk.read", Value = 100.5, Unit = SensorUnit.MBPerSecond },
                new() { MetricId = "disk.write", Value = 50.2, Unit = SensorUnit.MBPerSecond }
            }.AsReadOnly());

        var readings = await mock.Object.ReadAsync();

        Assert.Equal(2, readings.Count);
        Assert.Equal("disk.read", readings[0].MetricId);
        Assert.Equal(100.5, readings[0].Value);
    }

    [Fact]
    public void Provider_Name_IsNotEmpty()
    {
        var provider = new TestMockProvider("CpuProvider", new[] { "cpu.usage" });
        Assert.False(string.IsNullOrWhiteSpace(provider.Name));
    }

    [Fact]
    public void Provider_Dispose_DoesNotThrow()
    {
        var provider = new TestMockProvider("Disposable", new[] { "test" });
        var ex = Record.Exception(() => provider.Dispose());
        Assert.Null(ex);
    }
}
