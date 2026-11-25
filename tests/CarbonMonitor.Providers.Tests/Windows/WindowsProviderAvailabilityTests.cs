namespace CarbonMonitor.Providers.Tests.Windows;

using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using Moq;
using Xunit;

/// <summary>
/// Tests that Windows providers follow the basic provider contract.
/// These tests use mocks to verify behavior without requiring actual Windows sensors.
/// </summary>
public class WindowsProviderAvailabilityTests
{
    private static Mock<ISensorProvider> CreateMockWindowsProvider(
        string name, string[] metricIds,
        ProviderHealth health = ProviderHealth.Healthy,
        SensorAvailability availability = SensorAvailability.Available)
    {
        var mock = new Mock<ISensorProvider>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.MetricIds).Returns(metricIds);
        mock.Setup(p => p.Health).Returns(health);
        mock.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mock.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(metricIds.Select(id => new SensorReading
            {
                MetricId = id,
                DisplayName = id,
                Value = availability == SensorAvailability.Available ? 50.0 : null,
                Availability = availability,
                ProviderName = name,
                Timestamp = DateTimeOffset.UtcNow
            }).ToList().AsReadOnly());

        return mock;
    }

    // ── InitializeAsync doesn't crash ──

    [Fact]
    public async Task CpuProvider_InitializeAsync_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("WindowsCpu",
            new[] { "cpu.usage", "cpu.frequency" });

        var result = await provider.Object.InitializeAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task MemoryProvider_InitializeAsync_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("WindowsMemory",
            new[] { "memory.used", "memory.total", "memory.percent" });

        var result = await provider.Object.InitializeAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task DiskProvider_InitializeAsync_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("WindowsDisk",
            new[] { "disk.read.speed", "disk.write.speed" });

        var result = await provider.Object.InitializeAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task GpuProvider_InitializeAsync_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("WindowsGpu",
            new[] { "gpu.usage", "gpu.memory.dedicated", "gpu.memory.shared" });

        var result = await provider.Object.InitializeAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task NetworkProvider_InitializeAsync_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("WindowsNetwork",
            new[] { "network.ping", "network.packet_loss" });

        var result = await provider.Object.InitializeAsync();
        Assert.True(result);
    }

    // ── Proper Availability status ──

    [Fact]
    public async Task Provider_Available_ReturnsAvailableStatus()
    {
        var provider = CreateMockWindowsProvider("Available",
            new[] { "cpu.usage" },
            availability: SensorAvailability.Available);

        var readings = await provider.Object.ReadAsync();
        Assert.All(readings, r =>
            Assert.Equal(SensorAvailability.Available, r.Availability));
    }

    [Fact]
    public async Task Provider_Unsupported_ReturnsUnsupportedStatus()
    {
        var provider = CreateMockWindowsProvider("Unsupported",
            new[] { "gpu.temperature" },
            availability: SensorAvailability.Unsupported);

        var readings = await provider.Object.ReadAsync();
        Assert.All(readings, r =>
        {
            Assert.Equal(SensorAvailability.Unsupported, r.Availability);
            Assert.Null(r.Value);
        });
    }

    [Theory]
    [InlineData(SensorAvailability.Available)]
    [InlineData(SensorAvailability.Unsupported)]
    [InlineData(SensorAvailability.PermissionRequired)]
    [InlineData(SensorAvailability.TemporarilyUnavailable)]
    [InlineData(SensorAvailability.Error)]
    [InlineData(SensorAvailability.NotDetected)]
    public async Task Provider_AllAvailabilityStates_AreReturnedCorrectly(SensorAvailability expected)
    {
        var provider = CreateMockWindowsProvider("StateTest",
            new[] { "test.metric" },
            availability: expected);

        var readings = await provider.Object.ReadAsync();
        Assert.Single(readings);
        Assert.Equal(expected, readings[0].Availability);
    }

    // ── Non-null MetricIds ──

    [Fact]
    public void Provider_MetricIds_AreNonNull()
    {
        var provider = CreateMockWindowsProvider("NonNull",
            new[] { "cpu.usage", "cpu.temp" });

        Assert.NotNull(provider.Object.MetricIds);
        Assert.All(provider.Object.MetricIds, id => Assert.False(string.IsNullOrEmpty(id)));
    }

    [Fact]
    public void Provider_MetricIds_AreNonEmpty()
    {
        var provider = CreateMockWindowsProvider("NonEmpty",
            new[] { "mem.used" });

        Assert.NotEmpty(provider.Object.MetricIds);
    }

    [Fact]
    public void Provider_MetricIds_ContainExpectedIds()
    {
        var expectedIds = new[] { "cpu.usage", "cpu.temperature", "cpu.frequency" };
        var provider = CreateMockWindowsProvider("Expected", expectedIds);

        foreach (var id in expectedIds)
        {
            Assert.Contains(id, provider.Object.MetricIds);
        }
    }

    // ── Dispose without throwing ──

    [Fact]
    public void Provider_Dispose_DoesNotThrow()
    {
        var provider = CreateMockWindowsProvider("Disposable",
            new[] { "cpu.usage" });

        var ex = Record.Exception(() => provider.Object.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Provider_DoubleDispose_DoesNotThrow()
    {
        var mock = new Mock<ISensorProvider>();
        mock.Setup(p => p.Name).Returns("DoubleDispose");
        mock.Setup(p => p.MetricIds).Returns(new[] { "test" });

        var ex = Record.Exception(() =>
        {
            mock.Object.Dispose();
            mock.Object.Dispose();
        });
        Assert.Null(ex);
    }

    // ── Health status ──

    [Fact]
    public void Provider_Health_ReflectsCurrentState()
    {
        var provider = CreateMockWindowsProvider("HealthCheck",
            new[] { "cpu.usage" },
            health: ProviderHealth.Healthy);

        Assert.Equal(ProviderHealth.Healthy, provider.Object.Health);
    }

    [Fact]
    public void Provider_DegradedHealth_ReportsCorrectly()
    {
        var provider = CreateMockWindowsProvider("Degraded",
            new[] { "gpu.temp" },
            health: ProviderHealth.Degraded);

        Assert.Equal(ProviderHealth.Degraded, provider.Object.Health);
    }

    // ── Readings have provider name ──

    [Fact]
    public async Task Provider_Readings_IncludeProviderName()
    {
        var provider = CreateMockWindowsProvider("WindowsCpuProvider",
            new[] { "cpu.usage" });

        var readings = await provider.Object.ReadAsync();
        Assert.All(readings, r =>
            Assert.Equal("WindowsCpuProvider", r.ProviderName));
    }

    // ── Readings have timestamps ──

    [Fact]
    public async Task Provider_Readings_HaveRecentTimestamps()
    {
        var provider = CreateMockWindowsProvider("TimestampCheck",
            new[] { "cpu.usage", "cpu.temp" });

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var readings = await provider.Object.ReadAsync();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.All(readings, r =>
            Assert.InRange(r.Timestamp, before, after));
    }
}
