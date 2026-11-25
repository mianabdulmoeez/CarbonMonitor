namespace CarbonMonitor.Core.Tests.Models;

using CarbonMonitor.Core.Models;
using Xunit;

public class SensorReadingTests
{
    // ── DisplayValue formatting ──

    [Theory]
    [InlineData(72.5, SensorUnit.Celsius, "72.5 °C")]
    [InlineData(0.0, SensorUnit.Celsius, "0.0 °C")]
    [InlineData(-40.0, SensorUnit.Celsius, "-40.0 °C")]
    [InlineData(100.0, SensorUnit.Celsius, "100.0 °C")]
    public void DisplayValue_Celsius_FormatsCorrectly(double value, SensorUnit unit, string expected)
    {
        var reading = new SensorReading { Value = value, Unit = unit };
        Assert.Equal(expected, reading.DisplayValue);
    }

    [Theory]
    [InlineData(161.5, SensorUnit.Fahrenheit, "161.5 °F")]
    [InlineData(32.0, SensorUnit.Fahrenheit, "32.0 °F")]
    public void DisplayValue_Fahrenheit_FormatsCorrectly(double value, SensorUnit unit, string expected)
    {
        var reading = new SensorReading { Value = value, Unit = unit };
        Assert.Equal(expected, reading.DisplayValue);
    }

    [Theory]
    [InlineData(45.2, SensorUnit.Percent, "45.2 %")]
    [InlineData(100.0, SensorUnit.Percent, "100.0 %")]
    [InlineData(0.0, SensorUnit.Percent, "0.0 %")]
    public void DisplayValue_Percent_FormatsCorrectly(double value, SensorUnit unit, string expected)
    {
        var reading = new SensorReading { Value = value, Unit = unit };
        Assert.Equal(expected, reading.DisplayValue);
    }

    [Theory]
    [InlineData(1234.0, SensorUnit.MHz, "1234 MHz")]
    [InlineData(3600.0, SensorUnit.MHz, "3600 MHz")]
    [InlineData(0.0, SensorUnit.MHz, "0 MHz")]
    public void DisplayValue_MHz_FormatsAsInteger(double value, SensorUnit unit, string expected)
    {
        var reading = new SensorReading { Value = value, Unit = unit };
        Assert.Equal(expected, reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_GHz_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 3.6, Unit = SensorUnit.GHz };
        Assert.Equal("3.6 GHz", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_MB_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 8192.5, Unit = SensorUnit.MB };
        Assert.Equal("8192.5 MB", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_GB_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 15.6, Unit = SensorUnit.GB };
        Assert.Equal("15.6 GB", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_MBPerSecond_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 250.3, Unit = SensorUnit.MBPerSecond };
        Assert.Equal("250.3 MB/s", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_Rpm_FormatsAsInteger()
    {
        var reading = new SensorReading { Value = 2400.0, Unit = SensorUnit.Rpm };
        Assert.Equal("2400 RPM", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_Fps_FormatsAsInteger()
    {
        var reading = new SensorReading { Value = 144.7, Unit = SensorUnit.Fps };
        Assert.Equal("145 FPS", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_Milliseconds_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 6.9, Unit = SensorUnit.Milliseconds };
        Assert.Equal("6.9 ms", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_Watts_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 15.2, Unit = SensorUnit.Watts };
        Assert.Equal("15.2 W", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_Volts_FormatsWithOneDecimal()
    {
        var reading = new SensorReading { Value = 1.2, Unit = SensorUnit.Volts };
        Assert.Equal("1.2 V", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_None_NoUnitSuffix()
    {
        var reading = new SensorReading { Value = 42.0, Unit = SensorUnit.None };
        Assert.Equal("42", reading.DisplayValue);
    }

    // ── Null Value ──

    [Fact]
    public void DisplayValue_NullValue_ReturnsNA()
    {
        var reading = new SensorReading { Value = null, Unit = SensorUnit.Celsius };
        Assert.Equal("N/A", reading.DisplayValue);
    }

    [Fact]
    public void DisplayValue_NullValue_ReturnsNA_ForAllUnits()
    {
        foreach (SensorUnit unit in Enum.GetValues<SensorUnit>())
        {
            var reading = new SensorReading { Value = null, Unit = unit };
            Assert.Equal("N/A", reading.DisplayValue);
        }
    }

    // ── Unavailable factory method ──

    [Fact]
    public void Unavailable_SetsCorrectDefaults()
    {
        var reading = SensorReading.Unavailable(
            "gpu.temperature", "GPU Temperature",
            SensorCategory.Gpu, "Sensor not exposed on Intel UHD 620",
            "WindowsProvider");

        Assert.Equal("gpu.temperature", reading.MetricId);
        Assert.Equal("GPU Temperature", reading.DisplayName);
        Assert.Null(reading.Value);
        Assert.Equal(SensorCategory.Gpu, reading.Category);
        Assert.Equal(SensorAvailability.Unsupported, reading.Availability);
        Assert.Equal("Sensor not exposed on Intel UHD 620", reading.UnavailableReason);
        Assert.Equal("WindowsProvider", reading.ProviderName);
        Assert.Equal("N/A", reading.DisplayValue);
    }

    [Fact]
    public void Unavailable_DefaultProviderName_IsEmpty()
    {
        var reading = SensorReading.Unavailable(
            "test.metric", "Test", SensorCategory.Cpu, "Not available");
        Assert.Equal(string.Empty, reading.ProviderName);
    }

    [Fact]
    public void Unavailable_HasRecentTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var reading = SensorReading.Unavailable("m", "M", SensorCategory.Cpu, "r");
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(reading.Timestamp, before, after);
    }

    // ── SensorCategory coverage ──

    [Theory]
    [InlineData(SensorCategory.Cpu)]
    [InlineData(SensorCategory.Gpu)]
    [InlineData(SensorCategory.Memory)]
    [InlineData(SensorCategory.Storage)]
    [InlineData(SensorCategory.Network)]
    [InlineData(SensorCategory.Gaming)]
    [InlineData(SensorCategory.Cooling)]
    public void SensorCategory_AllValues_CanBeAssigned(SensorCategory category)
    {
        var reading = new SensorReading { Category = category };
        Assert.Equal(category, reading.Category);
    }

    [Fact]
    public void SensorCategory_AllEnumValues_AreCovered()
    {
        var values = Enum.GetValues<SensorCategory>();
        Assert.Equal(7, values.Length); // Cpu, Gpu, Memory, Storage, Network, Gaming, Cooling
    }

    // ── SensorAvailability transitions ──

    [Fact]
    public void SensorAvailability_DefaultIsAvailable()
    {
        var reading = new SensorReading();
        Assert.Equal(SensorAvailability.Available, reading.Availability);
    }

    [Theory]
    [InlineData(SensorAvailability.Available)]
    [InlineData(SensorAvailability.Unsupported)]
    [InlineData(SensorAvailability.PermissionRequired)]
    [InlineData(SensorAvailability.TemporarilyUnavailable)]
    [InlineData(SensorAvailability.Error)]
    [InlineData(SensorAvailability.NotDetected)]
    public void SensorAvailability_AllStates_CanBeSet(SensorAvailability availability)
    {
        var reading = new SensorReading { Availability = availability };
        Assert.Equal(availability, reading.Availability);
    }

    [Fact]
    public void SensorAvailability_Transition_AvailableToError()
    {
        var reading = new SensorReading { Availability = SensorAvailability.Available };
        reading.Availability = SensorAvailability.Error;
        Assert.Equal(SensorAvailability.Error, reading.Availability);
    }

    [Fact]
    public void SensorAvailability_Transition_ErrorToAvailable()
    {
        var reading = new SensorReading { Availability = SensorAvailability.Error };
        reading.Availability = SensorAvailability.Available;
        Assert.Equal(SensorAvailability.Available, reading.Availability);
    }

    [Fact]
    public void SensorAvailability_Transition_TemporarilyUnavailableToAvailable()
    {
        var reading = new SensorReading
        {
            Availability = SensorAvailability.TemporarilyUnavailable,
            UnavailableReason = "Sensor initializing"
        };
        reading.Availability = SensorAvailability.Available;
        reading.UnavailableReason = null;

        Assert.Equal(SensorAvailability.Available, reading.Availability);
        Assert.Null(reading.UnavailableReason);
    }

    // ── Default property values ──

    [Fact]
    public void NewSensorReading_HasCorrectDefaults()
    {
        var reading = new SensorReading();

        Assert.Equal(string.Empty, reading.MetricId);
        Assert.Equal(string.Empty, reading.DisplayName);
        Assert.Null(reading.Value);
        Assert.Equal(SensorUnit.None, reading.Unit);
        Assert.Equal(SensorCategory.Cpu, reading.Category); // enum default
        Assert.Equal(string.Empty, reading.HardwareId);
        Assert.Equal(string.Empty, reading.HardwareName);
        Assert.Equal(string.Empty, reading.ProviderName);
        Assert.Equal(SensorAvailability.Available, reading.Availability);
        Assert.Null(reading.UnavailableReason);
        Assert.Equal(SensorSourceType.Measured, reading.SourceType);
        Assert.Null(reading.QualityNote);
    }

    [Fact]
    public void SensorReading_AllProperties_CanBeSet()
    {
        var now = DateTimeOffset.UtcNow;
        var reading = new SensorReading
        {
            MetricId = "cpu.usage",
            DisplayName = "CPU Usage",
            Value = 55.5,
            Unit = SensorUnit.Percent,
            Category = SensorCategory.Cpu,
            HardwareId = "cpu0",
            HardwareName = "Intel Core i7-8650U",
            ProviderName = "WindowsPerfCounter",
            Timestamp = now,
            Availability = SensorAvailability.Available,
            UnavailableReason = null,
            SourceType = SensorSourceType.OsCounter,
            QualityNote = "High confidence"
        };

        Assert.Equal("cpu.usage", reading.MetricId);
        Assert.Equal("CPU Usage", reading.DisplayName);
        Assert.Equal(55.5, reading.Value);
        Assert.Equal(SensorUnit.Percent, reading.Unit);
        Assert.Equal(SensorCategory.Cpu, reading.Category);
        Assert.Equal("cpu0", reading.HardwareId);
        Assert.Equal("Intel Core i7-8650U", reading.HardwareName);
        Assert.Equal("WindowsPerfCounter", reading.ProviderName);
        Assert.Equal(now, reading.Timestamp);
        Assert.Equal(SensorAvailability.Available, reading.Availability);
        Assert.Null(reading.UnavailableReason);
        Assert.Equal(SensorSourceType.OsCounter, reading.SourceType);
        Assert.Equal("High confidence", reading.QualityNote);
    }
}
