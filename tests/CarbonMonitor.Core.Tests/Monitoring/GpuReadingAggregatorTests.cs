using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using Xunit;

namespace CarbonMonitor.Core.Tests.Monitoring;

public class GpuReadingAggregatorTests
{
    [Fact]
    public void Aggregate_MergesDuplicateProvidersForSamePhysicalIntelUhd620()
    {
        const string pnp = @"PCI\VEN_8086&DEV_5917&SUBSYS_225C17AA&REV_07\3&11583659&0&10";
        var hardware = new List<HardwareInfo>
        {
            new("win.gpu0", "Intel(R) UHD Graphics 620", "Intel Corporation", "31.0.101.2135", SensorCategory.Gpu, "Integrated", pnp, "pci:ven_8086&dev_5917"),
            new("/gpu-intel-integrated/0", "Intel(R) UHD Graphics 620", "Intel", null, SensorCategory.Gpu),
            new("intel.igpu0", "Intel(R) UHD Graphics 620", "Intel", "31.0.101.2135", SensorCategory.Gpu, "Integrated", pnp, "pci:ven_8086&dev_5917")
        };

        var readings = new List<SensorReading>
        {
            new() { MetricId = "gpu.usage", Value = 14.5, Unit = SensorUnit.Percent, Category = SensorCategory.Gpu, HardwareId = "win.gpu0", HardwareName = "Intel(R) UHD Graphics 620", ProviderName = "Windows.GpuEngine" },
            new() { MetricId = "gpu.memory.shared", Value = 420.0, Unit = SensorUnit.MB, Category = SensorCategory.Gpu, HardwareId = "win.gpu0", HardwareName = "Intel(R) UHD Graphics 620", ProviderName = "Windows.GpuEngine" },
            new() { MetricId = "lhm.gpu.intel.load.d3d_3d", Value = 15.0, Unit = SensorUnit.Percent, Category = SensorCategory.Gpu, HardwareId = "/gpu-intel-integrated/0", HardwareName = "Intel(R) UHD Graphics 620", ProviderName = "Hardware.LibreHardwareMonitor" },
            SensorReading.Unavailable("intel.igpu.temperature", "Temp", SensorCategory.Gpu, "intel.igpu0", "Intel(R) UHD Graphics 620", "Hardware.Intel.IntegratedGpu", SensorAvailability.Unsupported, "No discrete sensor")
        };

        var aggregated = GpuReadingAggregator.Aggregate(readings, hardware);

        Assert.Single(aggregated);
        var gpu = aggregated[0];
        Assert.Equal("Intel(R) UHD Graphics 620", gpu.Name);
        Assert.Equal("31.0.101.2135", gpu.DriverVersion);
        Assert.True(gpu.IsIntegrated);
        Assert.Equal(14.5, gpu.Utilization?.Value);
        Assert.Equal(420.0, gpu.SharedMemoryUsed?.Value);
        Assert.Equal(SensorAvailability.Unsupported, gpu.Temperature?.Availability);
        Assert.Equal(3, gpu.ContributingProviders.Count);
    }

    [Fact]
    public void Aggregate_KeepsSeparatePhysicalGpusDistinctInHybridLaptop()
    {
        var hardware = new List<HardwareInfo>
        {
            new("win.gpu0", "Intel(R) UHD Graphics 620", "Intel", "31.0", SensorCategory.Gpu, null, @"PCI\VEN_8086&DEV_5917\0"),
            new("nvidia.gpu0", "NVIDIA GeForce RTX 3060 Laptop GPU", "NVIDIA", "551.86", SensorCategory.Gpu, null, @"PCI\VEN_10DE&DEV_2520\0")
        };

        var readings = new List<SensorReading>
        {
            new() { MetricId = "gpu.usage", Value = 5.0, Unit = SensorUnit.Percent, Category = SensorCategory.Gpu, HardwareId = "win.gpu0", HardwareName = "Intel(R) UHD Graphics 620", ProviderName = "Windows.GpuEngine" },
            new() { MetricId = "nvidia.gpu0.usage", Value = 78.0, Unit = SensorUnit.Percent, Category = SensorCategory.Gpu, HardwareId = "nvidia.gpu0", HardwareName = "NVIDIA GeForce RTX 3060 Laptop GPU", ProviderName = "Hardware.Nvidia.NVML" },
            new() { MetricId = "gpu.temperature", Value = 69.0, Unit = SensorUnit.Celsius, Category = SensorCategory.Gpu, HardwareId = "nvidia.gpu0", HardwareName = "NVIDIA GeForce RTX 3060 Laptop GPU", ProviderName = "Hardware.Nvidia.NVML" }
        };

        var aggregated = GpuReadingAggregator.Aggregate(readings, hardware);

        Assert.Equal(2, aggregated.Count);
        Assert.Contains(aggregated, g => g.Name.Contains("RTX 3060") && !g.IsIntegrated && g.Temperature?.Value == 69.0);
        Assert.Contains(aggregated, g => g.Name.Contains("UHD Graphics 620") && g.IsIntegrated && g.Temperature is null);
    }
}
