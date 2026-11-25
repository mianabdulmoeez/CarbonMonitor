namespace CarbonMonitor.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Manages loading, validating, migrating, saving, and exporting application settings.
/// </summary>
public class SettingsManager
{
    public const int CurrentConfigVersion = 1;

    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string SettingsFilePath => _settingsFilePath;

    public SettingsManager(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
    }

    /// <summary>Load settings from disk. Returns defaults if file doesn't exist or is invalid.</summary>
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsFilePath))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            return settings is null ? new AppSettings() : ValidateAndMigrate(settings);
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Save settings to disk atomically (write to temp, then replace).</summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = ValidateAndMigrate(settings);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempFile = _settingsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            File.Move(tempFile, _settingsFilePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Reset to default settings and save.</summary>
    public async Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = new AppSettings();
        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    /// <summary>Export settings to a file.</summary>
    public async Task ExportAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(exportPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Import settings from a file, validate/migrate, and persist.</summary>
    public async Task<AppSettings> ImportAsync(string importPath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(importPath, cancellationToken).ConfigureAwait(false);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        if (settings is null)
            throw new InvalidOperationException("Failed to deserialize settings from import file.");

        settings = ValidateAndMigrate(settings);
        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <summary>Validates safe ranges and migrates older configuration schema versions.</summary>
    public static AppSettings ValidateAndMigrate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ConfigVersion < CurrentConfigVersion)
        {
            settings.ConfigVersion = CurrentConfigVersion;
        }

        settings.Hotkey ??= new HotkeyConfig();
        if (!VirtualKeyHelper.IsValidHotkey(settings.Hotkey.Modifiers, settings.Hotkey.Key))
        {
            settings.Hotkey.Modifiers = VirtualKeyHelper.ModAlt;
            settings.Hotkey.Key = 82;
        }

        settings.Overlay ??= new OverlayConfig();
        settings.Overlay.FontSize = Math.Clamp(settings.Overlay.FontSize, 8, 36);
        settings.Overlay.TextOpacity = Math.Clamp(settings.Overlay.TextOpacity, 0.1, 1.0);
        settings.Overlay.BackgroundOpacity = Math.Clamp(settings.Overlay.BackgroundOpacity, 0.0, 1.0);
        settings.Overlay.DisplayIndex = Math.Max(0, settings.Overlay.DisplayIndex);
        if (string.IsNullOrWhiteSpace(settings.Overlay.FontFamily))
            settings.Overlay.FontFamily = "Segoe UI";
        if (string.IsNullOrWhiteSpace(settings.Overlay.Position))
            settings.Overlay.Position = "TopRight";

        settings.PollingIntervalMs = Math.Clamp(settings.PollingIntervalMs, 500, 30000);
        settings.ProviderTimeoutMs = Math.Clamp(settings.ProviderTimeoutMs, 1000, 30000);
        settings.BatteryPollingIntervalMs = Math.Clamp(settings.BatteryPollingIntervalMs, 1000, 60000);
        settings.AlertCooldownSeconds = Math.Clamp(settings.AlertCooldownSeconds, 10, 3600);
        settings.LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 365);
        settings.PingIntervalSeconds = Math.Clamp(settings.PingIntervalSeconds, 2, 60);

        if (string.IsNullOrWhiteSpace(settings.PingTarget))
            settings.PingTarget = "8.8.8.8";

        settings.Metrics ??= new List<MetricConfig>();
        settings.GameProcesses ??= new List<GameProcessConfig>();
        settings.LocalLogsOnly = true; // Privacy policy: never auto-upload telemetry

        return settings;
    }
}
