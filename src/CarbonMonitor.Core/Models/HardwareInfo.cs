namespace CarbonMonitor.Core.Models;

/// <summary>
/// Describes a detected physical or logical hardware component.
/// </summary>
public sealed record HardwareInfo(
    string Id,
    string Name,
    string Vendor,
    string? DriverVersion,
    SensorCategory Category,
    string? Details = null,
    string? PnpDeviceId = null,
    string? CanonicalId = null);
