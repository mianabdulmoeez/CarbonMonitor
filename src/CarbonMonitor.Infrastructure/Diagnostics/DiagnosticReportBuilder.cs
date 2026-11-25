using System.Runtime.InteropServices;
using System.Text;
using CarbonMonitor.Core.Models;

namespace CarbonMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Generates local human-readable hardware and sensor diagnostic reports.
/// Reports are strictly local and never uploaded automatically.
/// </summary>
public static class DiagnosticReportBuilder
{
    public static string BuildReport(
        IReadOnlyList<ProviderHealthInfo> providerHealth,
        IReadOnlyList<HardwareInfo> detectedHardware,
        IReadOnlyList<SensorReading> latestReadings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("======================================================================");
        sb.AppendLine("  CARBON MONITOR - HARDWARE & SENSOR DIAGNOSTIC REPORT");
        sb.AppendLine("======================================================================");
        sb.AppendLine($"  Generated:    {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"  App Version:  1.0.0");
        sb.AppendLine();

        sb.AppendLine("-- SYSTEM INFORMATION ------------------------------------------------");
        sb.AppendLine($"  OS:           {RuntimeInformation.OSDescription}");
        sb.AppendLine($"  OS Version:   {Environment.OSVersion}");
        sb.AppendLine($"  Architecture: {RuntimeInformation.OSArchitecture} / Process: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"  Machine Name: {Environment.MachineName}");
        sb.AppendLine($"  Username:     {Environment.UserName}");
        sb.AppendLine($"  Logical CPUs: {Environment.ProcessorCount}");
        sb.AppendLine($"  .NET Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();

        sb.AppendLine("-- DETECTED HARDWARE -------------------------------------------------");
        if (detectedHardware.Count == 0)
        {
            sb.AppendLine("  (No hardware descriptors reported)");
        }
        else
        {
            foreach (var hw in detectedHardware)
            {
                sb.AppendLine($"  [{hw.Category}] {hw.Name} (Vendor: {hw.Vendor}, Id: {hw.Id})");
                if (!string.IsNullOrWhiteSpace(hw.DriverVersion))
                    sb.AppendLine($"    Driver: {hw.DriverVersion}");
                if (!string.IsNullOrWhiteSpace(hw.Details))
                    sb.AppendLine($"    Details: {hw.Details}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("-- PROVIDER HEALTH ---------------------------------------------------");
        foreach (var h in providerHealth)
        {
            sb.AppendLine($"  [{(h.IsHealthy ? "HEALTHY" : h.Status.ToString().ToUpperInvariant())}] {h.ProviderName}");
            sb.AppendLine($"    Available: {h.IsAvailable} | Sensors: {h.SensorCount} | Checked: {h.LastChecked:HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(h.ErrorMessage))
                sb.AppendLine($"    Note: {h.ErrorMessage}");
        }
        sb.AppendLine();

        sb.AppendLine("-- SENSOR READINGS ---------------------------------------------------");
        foreach (var group in latestReadings.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            sb.AppendLine($"  --- {group.Key} ---");
            foreach (var r in group.OrderBy(x => x.MetricId))
            {
                sb.AppendLine($"    {r.MetricId,-42} {r.DisplayValue,-14} [{r.Availability}]");
                sb.AppendLine($"      Display:  {r.DisplayName}");
                sb.AppendLine($"      Hardware: {r.HardwareName} ({r.HardwareId})");
                sb.AppendLine($"      Provider: {r.ProviderName} | SourceType: {r.SourceType} | Timestamp: {r.Timestamp:HH:mm:ss.fff}");
                if (!string.IsNullOrWhiteSpace(r.UnavailableReason))
                    sb.AppendLine($"      Reason:   {r.UnavailableReason}");
                if (!string.IsNullOrWhiteSpace(r.QualityNote))
                    sb.AppendLine($"      Quality:  {r.QualityNote}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("======================================================================");
        sb.AppendLine("  PRIVACY NOTICE: This report is stored locally only.");
        sb.AppendLine("  Review for serial numbers, usernames, or machine identifiers");
        sb.AppendLine("  before sharing with anyone.");
        sb.AppendLine("======================================================================");

        return sb.ToString();
    }
}
