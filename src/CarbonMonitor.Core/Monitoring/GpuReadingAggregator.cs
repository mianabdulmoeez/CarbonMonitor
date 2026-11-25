using System.Text.RegularExpressions;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Core.Monitoring;

/// <summary>
/// Deduplicates GPU readings emitted by multiple providers (Windows PDH, LHM, Intel, NVML, ADLX)
/// into distinct physical GPU devices using canonical hardware identifiers (PnP/PCI ID or normalized model name).
/// Never merges genuinely separate GPUs (e.g., hybrid Intel UHD + NVIDIA RTX).
/// </summary>
public static partial class GpuReadingAggregator
{
    [GeneratedRegex(@"VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}", RegexOptions.IgnoreCase)]
    private static partial Regex PciVenDevRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    public static IReadOnlyList<PhysicalGpuSnapshot> Aggregate(
        IReadOnlyList<SensorReading> readings,
        IReadOnlyList<HardwareInfo> hardwareInfos)
    {
        var gpuHardware = hardwareInfos.Where(h => h.Category == SensorCategory.Gpu).ToList();
        var hwById = new Dictionary<string, HardwareInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var hw in gpuHardware)
            hwById.TryAdd(hw.Id, hw);

        var snapshots = new Dictionary<string, PhysicalGpuSnapshot>(StringComparer.OrdinalIgnoreCase);

        // Seed snapshots from HardwareInfo descriptors so driver/PnP metadata is attached
        foreach (var hw in gpuHardware)
        {
            var key = ResolveCanonicalKey(hw.Name, hw.PnpDeviceId, hw.CanonicalId);
            var snap = GetOrCreateSnapshot(snapshots, key, hw.Name, hw.Vendor, hw.DriverVersion, hw.PnpDeviceId);
            if (!string.IsNullOrWhiteSpace(hw.DriverVersion) && string.IsNullOrWhiteSpace(snap.DriverVersion))
                snap.DriverVersion = hw.DriverVersion;
            if (!string.IsNullOrWhiteSpace(hw.PnpDeviceId) && string.IsNullOrWhiteSpace(snap.PnpDeviceId))
                snap.PnpDeviceId = hw.PnpDeviceId;
        }

        // Merge GPU sensor readings (and GPU-specific cooling sensors like GPU fan)
        foreach (var reading in readings)
        {
            bool isGpuCooling = reading.Category == SensorCategory.Cooling &&
                                (reading.MetricId.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MetricId.Contains("igpu", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MetricId.Contains("nvidia", StringComparison.OrdinalIgnoreCase));

            if (reading.Category != SensorCategory.Gpu && !isGpuCooling)
                continue;

            hwById.TryGetValue(reading.HardwareId, out var matchedHw);
            var name = !string.IsNullOrWhiteSpace(reading.HardwareName)
                ? reading.HardwareName
                : matchedHw?.Name ?? "Unknown GPU";

            var key = ResolveCanonicalKey(name, matchedHw?.PnpDeviceId, matchedHw?.CanonicalId);
            var snap = GetOrCreateSnapshot(
                snapshots,
                key,
                name,
                matchedHw?.Vendor ?? InferVendor(name),
                matchedHw?.DriverVersion,
                matchedHw?.PnpDeviceId);

            snap.ContributingProviders.Add(reading.ProviderName);
            snap.AllReadings.Add(reading);
            MapReadingToSnapshot(snap, reading);
        }

        return snapshots.Values
            .OrderBy(s => s.IsVirtualOrSoftware)
            .ThenBy(s => s.IsIntegrated) // Dedicated GPUs first, integrated second, virtual last
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public static string ResolveCanonicalKey(string? hardwareName, string? pnpDeviceId, string? explicitCanonicalId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitCanonicalId))
            return explicitCanonicalId.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            var m = PciVenDevRegex().Match(pnpDeviceId);
            if (m.Success)
                return $"pci:{m.Value.ToLowerInvariant()}";
        }

        var normalized = NormalizeGpuName(hardwareName);
        return string.IsNullOrEmpty(normalized) ? "gpu:unknown" : $"name:{normalized}";
    }

    public static string NormalizeGpuName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        var cleaned = rawName
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Corporation", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();

        return SlugRegex().Replace(cleaned, " ").Trim();
    }

    private static PhysicalGpuSnapshot GetOrCreateSnapshot(
        Dictionary<string, PhysicalGpuSnapshot> map,
        string key,
        string name,
        string vendor,
        string? driver,
        string? pnp)
    {
        if (map.TryGetValue(key, out var existing))
        {
            // Prefer the richer display name (e.g., with (R)) if current is shorter
            if (name.Length > existing.Name.Length && !name.Contains("GPU 0", StringComparison.OrdinalIgnoreCase))
                existing.Name = name;
            return existing;
        }

        // Also match by normalized name if a provider lacked PnP ID (e.g., LHM vs WindowsGpuEngine)
        string normName = NormalizeGpuName(name);
        if (!string.IsNullOrEmpty(normName))
        {
            var byName = map.Values.FirstOrDefault(v => NormalizeGpuName(v.Name) == normName);
            if (byName is not null)
                return byName;
        }

        bool isVirtual = name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

        bool isIntegrated = IsIntegratedGpu(name, vendor);

        var created = new PhysicalGpuSnapshot
        {
            CanonicalKey = key,
            Name = name,
            Vendor = string.IsNullOrWhiteSpace(vendor) ? InferVendor(name) : vendor,
            DriverVersion = driver,
            PnpDeviceId = pnp,
            IsIntegrated = isIntegrated,
            IsVirtualOrSoftware = isVirtual
        };

        map[key] = created;
        return created;
    }

    private static void MapReadingToSnapshot(PhysicalGpuSnapshot snap, SensorReading r)
    {
        string id = r.MetricId.ToLowerInvariant();
        string dn = r.DisplayName.ToLowerInvariant();

        if (id is "gpu.usage" || id.EndsWith(".usage", StringComparison.Ordinal) ||
            id.Contains("d3d_3d", StringComparison.Ordinal) || dn.EndsWith("gpu core", StringComparison.Ordinal))
        {
            snap.Utilization = PreferAvailable(snap.Utilization, r, preferCanonical: id == "gpu.usage");
        }
        else if (id.Contains("temperature", StringComparison.Ordinal))
        {
            snap.Temperature = PreferAvailable(snap.Temperature, r);
        }
        else if (id.Contains("clock.memory", StringComparison.Ordinal))
        {
            snap.MemoryClock = PreferAvailable(snap.MemoryClock, r);
        }
        else if (id.Contains("clock", StringComparison.Ordinal))
        {
            snap.CoreClock = PreferAvailable(snap.CoreClock, r);
        }
        else if (id.Contains("vram.total", StringComparison.Ordinal) || id.Contains("dedicated_memory_total", StringComparison.Ordinal))
        {
            snap.DedicatedVramTotal = PreferAvailable(snap.DedicatedVramTotal, r);
        }
        else if (id.Contains("memory.dedicated", StringComparison.Ordinal) || id.Contains("vram.used", StringComparison.Ordinal) ||
                 id.Contains("dedicated_memory_used", StringComparison.Ordinal))
        {
            snap.DedicatedVramUsed = PreferAvailable(snap.DedicatedVramUsed, r);
        }
        else if (id.Contains("shared_memory_total", StringComparison.Ordinal))
        {
            snap.SharedMemoryTotal = PreferAvailable(snap.SharedMemoryTotal, r);
        }
        else if (id.Contains("memory.shared", StringComparison.Ordinal) || id.Contains("shared_memory_used", StringComparison.Ordinal))
        {
            snap.SharedMemoryUsed = PreferAvailable(snap.SharedMemoryUsed, r, preferCanonical: id == "gpu.memory.shared");
        }
        else if (id.Contains("fan", StringComparison.Ordinal))
        {
            snap.FanSpeed = PreferAvailable(snap.FanSpeed, r);
        }
        else if (id.Contains("power", StringComparison.Ordinal))
        {
            snap.PowerDraw = PreferAvailable(snap.PowerDraw, r);
        }

        if (id.Contains("gpu.engine.", StringComparison.Ordinal) || id.Contains("lhm.gpu.", StringComparison.Ordinal) && id.Contains(".load.", StringComparison.Ordinal))
        {
            snap.EngineReadings.Add(r);
        }
    }

    private static SensorReading PreferAvailable(SensorReading? current, SensorReading candidate, bool preferCanonical = false)
    {
        if (current is null) return candidate;
        if (candidate.Availability == SensorAvailability.Available && current.Availability != SensorAvailability.Available)
            return candidate;
        if (current.Availability == SensorAvailability.Available && candidate.Availability != SensorAvailability.Available)
            return current;
        if (preferCanonical && candidate.Availability == SensorAvailability.Available)
            return candidate;
        return current;
    }

    private static string InferVendor(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return "System";
    }

    private static bool IsIntegratedGpu(string name, string vendor)
    {
        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris", StringComparison.OrdinalIgnoreCase);
    }
}
