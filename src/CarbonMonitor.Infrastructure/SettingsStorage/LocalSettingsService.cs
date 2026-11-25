using CarbonMonitor.Core.Configuration;

namespace CarbonMonitor.Infrastructure.SettingsStorage;

/// <summary>
/// Application-level settings store bound to %LocalAppData%\CarbonMonitor\settings.json
/// with cached state, default metric population, and change notifications.
/// </summary>
public sealed class LocalSettingsService
{
    private readonly SettingsManager _inner;
    private AppSettings _current = new();

    public string SettingsPath => _inner.SettingsFilePath;
    public AppSettings Current => _current;

    public event Action<AppSettings>? SettingsChanged;

    public LocalSettingsService(string? customSettingsPath = null)
    {
        var path = customSettingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CarbonMonitor",
            "settings.json");

        _inner = new SettingsManager(path);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var loaded = await _inner.LoadAsync(ct).ConfigureAwait(false);
        loaded.EnsureDefaultMetrics();
        _current = loaded;
        return loaded;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.EnsureDefaultMetrics();
        await _inner.SaveAsync(settings, ct).ConfigureAwait(false);
        _current = settings;
        SettingsChanged?.Invoke(settings);
    }

    public async Task<AppSettings> ResetToDefaultsAsync(CancellationToken ct = default)
    {
        var defaults = new AppSettings();
        defaults.EnsureDefaultMetrics();
        await _inner.SaveAsync(defaults, ct).ConfigureAwait(false);
        _current = defaults;
        SettingsChanged?.Invoke(defaults);
        return defaults;
    }

    public Task ExportAsync(string targetPath, CancellationToken ct = default)
        => _inner.ExportAsync(targetPath, ct);

    public async Task<AppSettings> ImportAsync(string sourcePath, CancellationToken ct = default)
    {
        var imported = await _inner.ImportAsync(sourcePath, ct).ConfigureAwait(false);
        imported.EnsureDefaultMetrics();
        _current = imported;
        SettingsChanged?.Invoke(imported);
        return imported;
    }
}
