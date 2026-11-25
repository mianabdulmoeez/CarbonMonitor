using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace CarbonMonitor.Infrastructure.Elevation;

/// <summary>
/// Helpers for checking elevation status and performing an optional user-initiated
/// elevated restart when ring-0 hardware MSR / NVMe SMART sensors are desired.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevationHelper
{
    public static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRestartElevated(string executablePath, Action? releaseSingleInstanceLock, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            errorMessage = "Application executable path could not be resolved.";
            return false;
        }

        try
        {
            releaseSingleInstanceLock?.Invoke();

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
            };

            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User declined UAC prompt
            errorMessage = "UAC elevation prompt was cancelled by the user.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to restart elevated: {ex.Message}";
            return false;
        }
    }
}
