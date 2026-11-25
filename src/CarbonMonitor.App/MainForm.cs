using CarbonMonitor.App.Dashboard;
using CarbonMonitor.App.Diagnostics;
using CarbonMonitor.App.Hotkey;
using CarbonMonitor.App.Overlay;
using CarbonMonitor.App.Settings;
using CarbonMonitor.App.Tray;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using CarbonMonitor.Core.Scheduling;
using CarbonMonitor.Gaming.ProcessDetection;
using CarbonMonitor.Gaming.PresentMon;
using CarbonMonitor.Infrastructure.Elevation;
using CarbonMonitor.Infrastructure.Logging;
using CarbonMonitor.Infrastructure.SettingsStorage;
using CarbonMonitor.Infrastructure.SingleInstance;
using CarbonMonitor.Infrastructure.Startup;
using CarbonMonitor.Providers.Hardware.Amd;
using CarbonMonitor.Providers.Hardware.Intel;
using CarbonMonitor.Providers.Hardware.LibreHardwareMonitor;
using CarbonMonitor.Providers.Hardware.Nvidia;
using CarbonMonitor.Providers.Hardware.Nvme;
using CarbonMonitor.Providers.Hardware.ThinkPad;
using CarbonMonitor.Providers.Windows.Cpu;
using CarbonMonitor.Providers.Windows.Disk;
using CarbonMonitor.Providers.Windows.Gpu;
using CarbonMonitor.Providers.Windows.Memory;
using CarbonMonitor.Providers.Windows.Network;
using CarbonMonitor.Providers.Windows.Power;

namespace CarbonMonitor.App;

/// <summary>
/// Hidden application host coordinating tray, hotkey, overlay, dashboard, diagnostics,
/// monitoring engine, game detection, adaptive polling, rate-limited alerts, and optional elevated restart.
/// </summary>
public sealed class MainForm : Form
{
    private readonly SingleInstanceGuard? _instanceGuard;
    private readonly LocalSettingsService _settingsService = new();
    private readonly HotkeyManager _hotkeyManager = new();
    private readonly TrayManager _trayManager = new();
    private readonly OverlayForm _overlayForm = new();
    private readonly AlertEvaluator _alertEvaluator = new();

    private readonly GameProcessDetector _gameDetector;
    private readonly GameProcessWatcher _gameWatcher;
    private readonly PresentMonProvider _presentMonProvider;
    private readonly WindowsNetworkProvider _networkProvider;
    private readonly MonitoringEngine _engine;

    private DashboardForm? _dashboardForm;
    private DiagnosticsForm? _diagnosticsForm;
    private SettingsForm? _settingsForm;
    private AppSettings _settings = new();
    private bool _exiting;
    private bool _initialized;

    public MainForm(SingleInstanceGuard? instanceGuard = null)
    {
        _instanceGuard = instanceGuard;
        Text = SingleInstanceGuard.TrayHostWindowTitle;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Size = new Size(1, 1);
        Location = new Point(-32000, -32000);

        _gameDetector = new GameProcessDetector(new[] { "VALORANT-Win64-Shipping" });
        _gameWatcher = new GameProcessWatcher(_gameDetector);
        _presentMonProvider = new PresentMonProvider(_gameDetector, captureEnabled: false);
        _networkProvider = new WindowsNetworkProvider();

        var providers = new List<ISensorProvider>
        {
            new WindowsCpuProvider(),
            new WindowsMemoryProvider(),
            new WindowsDiskProvider(),
            _networkProvider,
            new WindowsGpuEngineProvider(),
            new LhmProvider(),
            new IntelGpuProvider(),
            new NvmlProvider(),
            new AmdAdlxProvider(),
            new StorageHealthProvider(),
            new ThinkPadCapabilityProvider(),
            _presentMonProvider
        };

        _engine = new MonitoringEngine(providers);
        _engine.ShouldPollPredicate = EvaluateShouldPoll;
        _engine.ReadingsUpdated += OnReadingsUpdated;
        _engine.ProviderError += (_, e) =>
            FileLogger.Instance.Warning("MonitoringEngine", $"Provider {e.Provider.Name} error: {e.Error.Message}");

        WireEvents();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
            CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_initialized)
        {
            _initialized = true;
            _ = InitializeLifecycleAsync();
        }
    }

    private void WireEvents()
    {
        _hotkeyManager.HotkeyPressed += () => BeginInvoke(ToggleOverlayFromUser);

        _trayManager.DashboardRequested += (_, _) => BeginInvoke(OpenDashboard);
        _trayManager.OverlayToggleRequested += (_, _) => BeginInvoke(ToggleOverlayFromUser);
        _trayManager.SettingsRequested += (_, _) => BeginInvoke(OpenSettings);
        _trayManager.DiagnosticsRequested += (_, _) => BeginInvoke(OpenDiagnostics);
        _trayManager.PauseToggleRequested += (_, _) => BeginInvoke(TogglePauseMonitoring);
        _trayManager.ExitRequested += (_, _) => BeginInvoke(ExitApplication);

        _overlayForm.CustomPositionChanged += async (_, pos) =>
        {
            _settings.Overlay.Position = "Custom";
            _settings.Overlay.CustomX = pos.X;
            _settings.Overlay.CustomY = pos.Y;
            await _settingsService.SaveAsync(_settings);
        };

        _gameWatcher.OnGameStarted += (name, pid) =>
        {
            FileLogger.Instance.Info("GameWatcher", $"Detected game process started: {name} (PID {pid})");
            _presentMonProvider.SetTargetProcess(name, (uint)pid);
            BeginInvoke(RefreshTrayState);
        };

        _gameWatcher.OnGameStopped += name =>
        {
            FileLogger.Instance.Info("GameWatcher", $"Detected game process stopped: {name}");
            _presentMonProvider.ClearTarget();
            BeginInvoke(RefreshTrayState);
        };
    }

    private async Task InitializeLifecycleAsync()
    {
        try
        {
            _hotkeyManager.Initialize(Handle);
            _settings = await _settingsService.LoadAsync();
            FileLogger.Instance.Configure(_settings.LogRetentionDays, _settings.VerboseLogging);
            FileLogger.Instance.Info("MainForm", $"Loaded settings (Mode={_settings.MonitoringMode}, Hotkey={_settings.Hotkey.DisplayString})");

            await ApplySettingsAsync(_settings, isStartup: true);
            await _engine.StartAsync();
            _gameWatcher.Start();

            FileLogger.Instance.Info("MainForm", $"MonitoringEngine started ({_engine.GetProviderHealthInfo().Count} providers)");

            if (_settings.StartOverlayVisible)
                _overlayForm.ShowOverlay();

            if (!_settings.StartMinimized)
                OpenDashboard();

            RefreshTrayState();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MainForm", $"Startup lifecycle error: {ex}");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (_hotkeyManager.ProcessMessage(m.Msg, m.WParam))
        {
            FileLogger.Instance.Debug("HotkeyManager", "WM_HOTKEY received");
            return;
        }

        if (SingleInstanceGuard.ActivationMessageId != 0 && m.Msg == (int)SingleInstanceGuard.ActivationMessageId)
        {
            FileLogger.Instance.Info("SingleInstance", "Activation message received from secondary launch; opening Dashboard");
            BeginInvoke(OpenDashboard);
            return;
        }

        base.WndProc(ref m);
    }

    private async Task ApplySettingsAsync(AppSettings settings, bool isStartup = false)
    {
        _settings = settings;
        _engine.Mode = settings.MonitoringMode;
        _engine.PollingIntervalMs = AdaptivePollingPolicy.GetEffectivePollingIntervalMs(
            settings, WindowsPowerStateProvider.GetCurrentPowerSource());

        _gameWatcher.UpdateWatchList(settings.GameProcesses.Select(g => g.ProcessName));
        _presentMonProvider.CaptureEnabled = settings.EnableFpsCapture;
        await _networkProvider.SetPingTargetAsync(settings.PingTarget, settings.PingIntervalSeconds);

        _overlayForm.ApplySettings(settings);
        _dashboardForm?.ApplySettings(settings);

        bool registered = _hotkeyManager.TryRegister((uint)settings.Hotkey.Modifiers, (uint)settings.Hotkey.Key);
        if (registered)
        {
            FileLogger.Instance.Info("HotkeyManager", $"Registered global hotkey: {settings.Hotkey.DisplayString}");
        }
        else if (!string.IsNullOrEmpty(_hotkeyManager.ConflictMessage))
        {
            FileLogger.Instance.Warning("HotkeyManager", _hotkeyManager.ConflictMessage);
            _trayManager.ShowBalloon("Carbon Monitor - Hotkey Conflict", _hotkeyManager.ConflictMessage, ToolTipIcon.Warning, 6000);
        }

        if (registered && isStartup && settings.StartMinimized)
        {
            _trayManager.ShowBalloon(
                "Carbon Monitor",
                $"Running quietly in the system tray. Press {settings.Hotkey.DisplayString} to toggle overlay.",
                ToolTipIcon.Info,
                3000);
        }

        var exePath = Application.ExecutablePath;
        if (settings.LaunchAtStartup && !StartupManager.IsStartupEnabled())
            StartupManager.EnableStartup(exePath);
        else if (!settings.LaunchAtStartup && StartupManager.IsStartupEnabled() && !isStartup)
            StartupManager.DisableStartup();

        RefreshTrayState();
    }

    private bool EvaluateShouldPoll()
    {
        var power = WindowsPowerStateProvider.GetCurrentPowerSource();
        _engine.PollingIntervalMs = AdaptivePollingPolicy.GetEffectivePollingIntervalMs(_settings, power);

        bool overlayVisible = _overlayForm.Visible;
        bool dashboardOpen = (_dashboardForm is { Visible: true, WindowState: not FormWindowState.Minimized }) ||
                             (_diagnosticsForm is { Visible: true, WindowState: not FormWindowState.Minimized });
        bool gameRunning = _gameWatcher.IsAnyGameRunning;

        return AdaptivePollingPolicy.ShouldPoll(_settings, overlayVisible, dashboardOpen, gameRunning, power);
    }

    private void OnReadingsUpdated(object? sender, IReadOnlyList<SensorReading> readings)
    {
        if (_exiting || IsDisposed) return;

        var alerts = _alertEvaluator.Evaluate(readings, _settings);

        try
        {
            BeginInvoke(() =>
            {
                if (_exiting || IsDisposed) return;
                if (_overlayForm.Visible)
                    _overlayForm.UpdateReadings(readings);

                foreach (var alert in alerts)
                {
                    _trayManager.ShowBalloon(
                        "Carbon Monitor Alert",
                        alert.Message,
                        alert.Severity == AlertSeverity.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // Window handle not created yet or already closing
        }
    }

    private void ToggleOverlayFromUser()
    {
        _overlayForm.ToggleOverlay();
        FileLogger.Instance.Info("Overlay", $"Overlay toggled -> Visible={_overlayForm.Visible}");
        if (_overlayForm.Visible && _engine.LatestReadings.Count > 0)
            _overlayForm.UpdateReadings(_engine.LatestReadings);
        RefreshTrayState();
    }

    private async void TogglePauseMonitoring()
    {
        if (_engine.State == EngineState.Paused)
            await _engine.ResumeAsync();
        else if (_engine.State == EngineState.Running)
            await _engine.PauseAsync();

        FileLogger.Instance.Info("MonitoringEngine", $"Monitoring state changed -> {_engine.State}");
        RefreshTrayState();
    }

    private void RefreshTrayState()
    {
        bool isPaused = _engine.State == EngineState.Paused;
        bool active = !isPaused && EvaluateShouldPoll();
        _trayManager.UpdateState(
            active,
            isPaused,
            _overlayForm.Visible,
            _settings.Hotkey.DisplayString,
            _settings.MonitoringMode.ToString());
    }

    private void OpenDashboard()
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = new DashboardForm(_engine);
            _dashboardForm.ApplySettings(_settings);
            _dashboardForm.ToggleOverlayRequested += (_, _) => ToggleOverlayFromUser();
            _dashboardForm.OpenSettingsRequested += (_, _) => OpenSettings();
            _dashboardForm.OpenDiagnosticsRequested += (_, _) => OpenDiagnostics();
            _dashboardForm.ElevateRequested += (_, _) => RestartElevatedForFullSensors();
            _dashboardForm.VisibleChanged += (_, _) => RefreshTrayState();
        }

        _dashboardForm.Show();
        if (_dashboardForm.WindowState == FormWindowState.Minimized)
            _dashboardForm.WindowState = FormWindowState.Normal;
        _dashboardForm.Activate();
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsForm is null || _diagnosticsForm.IsDisposed)
        {
            _diagnosticsForm = new DiagnosticsForm(_engine);
            _diagnosticsForm.ElevateRequested += (_, _) => RestartElevatedForFullSensors();
            _diagnosticsForm.VisibleChanged += (_, _) => RefreshTrayState();
        }

        _diagnosticsForm.Show();
        if (_diagnosticsForm.WindowState == FormWindowState.Minimized)
            _diagnosticsForm.WindowState = FormWindowState.Normal;
        _diagnosticsForm.Activate();
    }

    private void OpenSettings()
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Show();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsService, _hotkeyManager);
        _settingsForm.SettingsSaved += async (_, newSettings) => await ApplySettingsAsync(newSettings);
        _settingsForm.PreviewOverlayRequested += (_, previewSettings) =>
        {
            _overlayForm.ApplySettings(previewSettings);
            _overlayForm.ShowOverlay();
            _overlayForm.UpdateReadings(_engine.LatestReadings);
            RefreshTrayState();
        };
        _settingsForm.ElevateRequested += (_, _) => RestartElevatedForFullSensors();
        _settingsForm.Show();
    }

    private void RestartElevatedForFullSensors()
    {
        if (ElevationHelper.IsProcessElevated()) return;

        var confirm = MessageBox.Show(
            "Restart Carbon Monitor as Administrator to enable direct CPU Package MSR temperature, CPU power, and NVMe SMART SSD temperature?\n\n" +
            "Note: Intel UHD 620 still does not expose a separate GPU temperature sensor.",
            "Enable Elevated Sensors",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        if (ElevationHelper.TryRestartElevated(Application.ExecutablePath, () => _instanceGuard?.Dispose(), out var error))
        {
            FileLogger.Instance.Info("Elevation", "Restarting Carbon Monitor with elevated sensor access");
            ExitApplication();
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            FileLogger.Instance.Warning("Elevation", error);
            MessageBox.Show(error, "Elevation Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        _gameWatcher.Stop();
        await _engine.StopAsync();

        _dashboardForm?.ForceClose();
        _diagnosticsForm?.ForceClose();
        _settingsForm?.Close();
        _overlayForm.Close();

        Close();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyManager.Dispose();
            _trayManager.Dispose();
            _gameWatcher.Dispose();
            _engine.Dispose();
            _overlayForm.Dispose();
            _dashboardForm?.Dispose();
            _diagnosticsForm?.Dispose();
            _settingsForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}
