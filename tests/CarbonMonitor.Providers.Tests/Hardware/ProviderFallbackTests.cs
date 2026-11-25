using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Gaming.PresentMon;
using CarbonMonitor.Infrastructure.Diagnostics;
using CarbonMonitor.Providers.Hardware.Amd;
using CarbonMonitor.Providers.Hardware.Intel;
using CarbonMonitor.Providers.Hardware.Nvidia;
using Xunit;

namespace CarbonMonitor.Providers.Tests.Hardware;

public class ProviderFallbackTests
{
    [Fact]
    public async Task IntelGpuProvider_DoesNotThrow_AndNeverRelabelsCpuTemp()
    {
        using var provider = new IntelGpuProvider();
        await provider.InitializeAsync();
        var readings = await provider.ReadAsync();

        // Either unavailable (on non-Intel hosts) or explicitly reports SupportsTemperature = false
        Assert.False(provider.SupportsTemperature);
        foreach (var r in readings)
        {
            Assert.NotEqual(SensorAvailability.Available, r.Availability);
            Assert.Null(r.Value);
        }
    }

    [Fact]
    public async Task AmdAdlxProvider_DegradesGracefullyWithoutCrashing()
    {
        using var provider = new AmdAdlxProvider();
        await provider.InitializeAsync();
        var readings = await provider.ReadAsync();

        Assert.NotNull(readings);
        var health = provider.GetHealthInfo();
        Assert.Equal("Hardware.Amd.ADLX", health.ProviderName);
    }

    [Fact]
    public async Task NvmlProvider_DegradesGracefullyWithoutDriver()
    {
        using var provider = new NvmlProvider();
        await provider.InitializeAsync();
        var readings = await provider.ReadAsync();

        Assert.NotNull(readings);
        var health = provider.GetHealthInfo();
        Assert.Equal("Hardware.Nvidia.NVML", health.ProviderName);
    }

    [Fact]
    public async Task PresentMonProvider_DisabledByDefault_ReportsUnsupportedSafely()
    {
        using var provider = new PresentMonProvider(captureEnabled: false);
        await provider.InitializeAsync();
        var readings = await provider.ReadAsync();

        Assert.False(provider.SupportsFps);
        Assert.NotEmpty(readings);
        Assert.All(readings, r => Assert.Equal(SensorAvailability.Unsupported, r.Availability));
    }

    [Fact]
    public void DiagnosticReportBuilder_IncludesPrivacyNoticeAndSensorDetails()
    {
        var health = new[]
        {
            new ProviderHealthInfo("Windows.Cpu", ProviderHealth.Healthy, null, DateTimeOffset.UtcNow, 2, true)
        };
        var hw = new[]
        {
            new HardwareInfo("cpu0", "Intel Core i7-8650U", "Intel", null, SensorCategory.Cpu)
        };
        var readings = new[]
        {
            new SensorReading
            {
                MetricId = "cpu.usage.total",
                DisplayName = "CPU Usage",
                Value = 12.5,
                Unit = SensorUnit.Percent,
                Category = SensorCategory.Cpu,
                HardwareId = "cpu0",
                HardwareName = "Intel Core i7-8650U",
                ProviderName = "Windows.Cpu"
            }
        };

        var report = DiagnosticReportBuilder.BuildReport(health, hw, readings);

        Assert.Contains("PRIVACY NOTICE", report);
        Assert.Contains("Intel Core i7-8650U", report);
        Assert.Contains("cpu.usage.total", report);
        Assert.Contains("12.5 %", report);
    }
}
