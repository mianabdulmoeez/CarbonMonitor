namespace CarbonMonitor.Core.Tests.Configuration;

using System.Text.Json;
using CarbonMonitor.Core.Configuration;
using Xunit;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CarbonMonitorTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* cleanup best-effort */ }
    }

    // ── LoadAsync ──

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsDefaults()
    {
        var manager = new SettingsManager(_settingsPath);
        var settings = await manager.LoadAsync();

        Assert.NotNull(settings);
        Assert.Equal(1, settings.ConfigVersion);
        Assert.Equal(MonitoringMode.Always, settings.MonitoringMode);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_settingsPath, "this is not valid json {{{");
        var manager = new SettingsManager(_settingsPath);

        var settings = await manager.LoadAsync();

        Assert.NotNull(settings);
        Assert.Equal(1, settings.ConfigVersion);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_settingsPath, "");
        var manager = new SettingsManager(_settingsPath);

        var settings = await manager.LoadAsync();

        Assert.NotNull(settings);
    }

    // ── SaveAsync ──

    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var manager = new SettingsManager(_settingsPath);
        var settings = new AppSettings { PollingIntervalMs = 3000 };

        await manager.SaveAsync(settings);

        Assert.True(File.Exists(_settingsPath));
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "dir", "settings.json");
        var manager = new SettingsManager(nestedPath);
        var settings = new AppSettings();

        await manager.SaveAsync(settings);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public async Task SaveAsync_WritesValidJson()
    {
        var manager = new SettingsManager(_settingsPath);
        var settings = new AppSettings { PollingIntervalMs = 4000 };

        await manager.SaveAsync(settings);

        var json = await File.ReadAllTextAsync(_settingsPath);
        var parsed = JsonDocument.Parse(json);
        Assert.NotNull(parsed);
    }

    // ── SaveAsync + LoadAsync roundtrip ──

    [Fact]
    public async Task SaveAndLoad_Roundtrip_PreservesSettings()
    {
        var manager = new SettingsManager(_settingsPath);
        var original = new AppSettings
        {
            ConfigVersion = 1,
            MonitoringMode = MonitoringMode.GameOnly,
            PollingIntervalMs = 3000,
            LaunchAtStartup = true,
            Hotkey = new HotkeyConfig { Modifiers = 6, Key = 121 },
            GameProcesses = new List<GameProcessConfig>
            {
                new() { ProcessName = "cs2", DisplayName = "CS2" }
            }
        };

        await manager.SaveAsync(original);
        var loaded = await manager.LoadAsync();

        Assert.Equal(original.ConfigVersion, loaded.ConfigVersion);
        Assert.Equal(original.MonitoringMode, loaded.MonitoringMode);
        Assert.Equal(original.PollingIntervalMs, loaded.PollingIntervalMs);
        Assert.Equal(original.LaunchAtStartup, loaded.LaunchAtStartup);
        Assert.Equal(original.Hotkey.Modifiers, loaded.Hotkey.Modifiers);
        Assert.Equal(original.Hotkey.Key, loaded.Hotkey.Key);
        Assert.Single(loaded.GameProcesses);
        Assert.Equal("cs2", loaded.GameProcesses[0].ProcessName);
    }

    // ── Atomic write ──

    [Fact]
    public async Task SaveAsync_AtomicWrite_NoTempFileRemains()
    {
        var manager = new SettingsManager(_settingsPath);
        var settings = new AppSettings();

        await manager.SaveAsync(settings);

        var tempFile = _settingsPath + ".tmp";
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        var manager = new SettingsManager(_settingsPath);

        await manager.SaveAsync(new AppSettings { PollingIntervalMs = 1000 });
        await manager.SaveAsync(new AppSettings { PollingIntervalMs = 5000 });

        var loaded = await manager.LoadAsync();
        Assert.Equal(5000, loaded.PollingIntervalMs);
    }

    // ── ResetToDefaults ──

    [Fact]
    public async Task ResetToDefaults_RestoresDefaultSettings()
    {
        var manager = new SettingsManager(_settingsPath);

        // First save custom settings
        await manager.SaveAsync(new AppSettings
        {
            PollingIntervalMs = 9999,
            MonitoringMode = MonitoringMode.GameOnly
        });

        // Then reset
        var defaults = await manager.ResetToDefaultsAsync();

        Assert.Equal(2000, defaults.PollingIntervalMs);
        Assert.Equal(MonitoringMode.Always, defaults.MonitoringMode);

        // Verify file is updated too
        var loaded = await manager.LoadAsync();
        Assert.Equal(2000, loaded.PollingIntervalMs);
    }

    // ── Export/Import roundtrip ──

    [Fact]
    public async Task ExportImport_Roundtrip_PreservesSettings()
    {
        var manager = new SettingsManager(_settingsPath);
        var exportPath = Path.Combine(_tempDir, "export.json");

        var original = new AppSettings
        {
            PollingIntervalMs = 7777,
            MonitoringMode = MonitoringMode.OverlayOnly,
            Hotkey = new HotkeyConfig { Modifiers = 4, Key = 112 }
        };

        await manager.SaveAsync(original);
        await manager.ExportAsync(exportPath);

        Assert.True(File.Exists(exportPath));

        // Clear settings
        await manager.SaveAsync(new AppSettings());

        // Import
        var imported = await manager.ImportAsync(exportPath);

        Assert.Equal(7777, imported.PollingIntervalMs);
        Assert.Equal(MonitoringMode.OverlayOnly, imported.MonitoringMode);
        Assert.Equal(4, imported.Hotkey.Modifiers);
    }

    [Fact]
    public async Task ExportAsync_CreatesReadableJson()
    {
        var manager = new SettingsManager(_settingsPath);
        var exportPath = Path.Combine(_tempDir, "export.json");

        await manager.SaveAsync(new AppSettings { PollingIntervalMs = 2500 });
        await manager.ExportAsync(exportPath);

        var json = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("pollingIntervalMs", json); // camelCase
    }

    // ── Concurrent access ──

    [Fact]
    public async Task ConcurrentAccess_DoesNotCorrupt()
    {
        var manager = new SettingsManager(_settingsPath);

        // Run multiple saves and loads concurrently
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var settings = new AppSettings { PollingIntervalMs = 1000 + i };
            await manager.SaveAsync(settings);
            var loaded = await manager.LoadAsync();
            Assert.NotNull(loaded);
            Assert.InRange(loaded.PollingIntervalMs, 1000, 1009);
        });

        await Task.WhenAll(tasks);

        // Final load should succeed and return valid settings
        var final = await manager.LoadAsync();
        Assert.NotNull(final);
        Assert.InRange(final.PollingIntervalMs, 1000, 1009);
    }

    [Fact]
    public async Task ConcurrentAccess_MultipleReaders_Succeed()
    {
        var manager = new SettingsManager(_settingsPath);
        await manager.SaveAsync(new AppSettings { PollingIntervalMs = 5555 });

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var loaded = await manager.LoadAsync();
            Assert.Equal(5555, loaded.PollingIntervalMs);
        });

        await Task.WhenAll(tasks);
    }

    // ── Edge cases ──

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsManager(null!));
    }

    [Fact]
    public async Task SaveAsync_NullSettings_ThrowsArgumentNullException()
    {
        var manager = new SettingsManager(_settingsPath);
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.SaveAsync(null!));
    }
}
