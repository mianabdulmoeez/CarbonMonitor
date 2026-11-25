using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CarbonMonitor.Infrastructure.Startup;

/// <summary>
/// Manages opt-in per-user Windows sign-in startup registration under HKCU Run.
/// Touches only Carbon Monitor's own registry entry.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CarbonMonitor";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnableStartup(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return true;

            if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
