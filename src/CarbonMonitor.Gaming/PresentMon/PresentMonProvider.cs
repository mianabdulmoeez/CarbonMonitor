using System.Diagnostics;
using System.Globalization;
using CarbonMonitor.Core.Abstractions;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Gaming.FpsTracking;
using CarbonMonitor.Gaming.ProcessDetection;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CarbonMonitor.Gaming.PresentMon;

/// <summary>
/// Optional external/ETW-based FPS and frame-time provider.
/// Supports Intel PresentMon CLI (if PresentMon.exe is placed alongside the app)
/// or documented Windows DXGI ETW events via Microsoft.Diagnostics.Tracing.TraceEvent.
/// Does NOT inject DLLs, hook game functions, read game memory, or attempt anti-cheat evasion.
/// </summary>
public sealed class PresentMonProvider : IGameTelemetryProvider
{
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

    private readonly FpsCalculator _calculator = new(300);
    private readonly GameProcessDetector _detector;
    private readonly object _sync = new();

    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private Process? _presentMonProcess;
    private TraceEventSession? _etwSession;

    private volatile bool _captureEnabled;
    private volatile uint _targetPid;
    private volatile string? _targetProcessName;
    private string? _presentMonExePath;
    private bool _permissionDenied;
    private DateTimeOffset _lastFrameTimestamp = DateTimeOffset.MinValue;

    public string Name => "Gaming.PresentMon";
    public string Description => "Optional ETW / PresentMon FPS and frame-time telemetry (no injection)";
    public IReadOnlyList<string> MetricIds { get; } = new[]
    {
        "game.fps",
        "game.frametime",
        "game.fps.1percent_low"
    };

    public ProviderHealth Health { get; private set; } = ProviderHealth.NotInitialized;
    public string? HealthReason { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool SupportsFps => IsAvailable && _captureEnabled;
    public bool SupportsFrameTime => IsAvailable && _captureEnabled;

    public bool CaptureEnabled
    {
        get => _captureEnabled;
        set
        {
            if (_captureEnabled == value) return;
            _captureEnabled = value;
            if (!value)
            {
                StopCapture();
                Health = IsAvailable ? ProviderHealth.Healthy : Health;
                HealthReason = "FPS capture disabled in settings";
            }
            else if (_targetPid != 0)
            {
                RestartCapture();
            }
        }
    }

    public PresentMonProvider(GameProcessDetector? detector = null, bool captureEnabled = false)
    {
        _detector = detector ?? new GameProcessDetector(new[] { "VALORANT-Win64-Shipping" });
        _captureEnabled = captureEnabled;
    }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            Health = ProviderHealth.Faulted;
            HealthReason = "ETW / PresentMon telemetry is Windows-only.";
            return Task.FromResult(false);
        }

        _presentMonExePath = LocatePresentMonExecutable();
        IsAvailable = true;
        Health = ProviderHealth.Healthy;
        HealthReason = _captureEnabled
            ? (_presentMonExePath is not null
                ? $"Ready using PresentMon CLI ({Path.GetFileName(_presentMonExePath)})"
                : "Ready using Windows DXGI ETW session (requires elevation or Performance Log Users)")
            : "FPS capture disabled in settings";

        return Task.FromResult(true);
    }

    public Task<bool> IsGameRunningAsync(string processName, CancellationToken cancellationToken = default)
        => Task.FromResult(_detector.IsProcessRunning(processName));

    public void SetTargetProcess(string processName, uint pid)
    {
        lock (_sync)
        {
            if (_targetPid == pid && string.Equals(_targetProcessName, processName, StringComparison.OrdinalIgnoreCase))
                return;

            _targetProcessName = processName;
            _targetPid = pid;
            _permissionDenied = false;
            _calculator.Reset();

            if (_captureEnabled && pid != 0)
            {
                RestartCapture();
            }
        }
    }

    public void ClearTarget()
    {
        lock (_sync)
        {
            _targetPid = 0;
            _targetProcessName = null;
            StopCapture();
            _calculator.Reset();
        }
    }

    public Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<SensorReading>(3);
        var now = DateTimeOffset.UtcNow;
        const string qualityNote =
            "External ETW telemetry (no DLL injection/hooks). Vanguard and some anti-cheats are not guaranteed compatible.";

        if (!IsAvailable)
        {
            readings.Add(SensorReading.Unavailable("game.fps", "FPS", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.Unsupported,
                HealthReason ?? "PresentMon telemetry unavailable"));
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        if (!_captureEnabled)
        {
            readings.Add(SensorReading.Unavailable("game.fps", "FPS", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.Unsupported,
                "FPS capture is disabled in Settings > Game."));
            readings.Add(SensorReading.Unavailable("game.frametime", "Frame Time", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.Unsupported,
                "FPS capture is disabled in Settings > Game."));
            readings.Add(SensorReading.Unavailable("game.fps.1percent_low", "1% Low FPS", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.Unsupported,
                "FPS capture is disabled in Settings > Game."));
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        if (_permissionDenied)
        {
            var reason = HealthReason ?? "ETW session denied. Run elevated, join 'Performance Log Users', or place PresentMon.exe in app directory.";
            readings.Add(SensorReading.Unavailable("game.fps", "FPS", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "PresentMon ETW", Name, SensorAvailability.PermissionRequired,
                reason, qualityNote));
            readings.Add(SensorReading.Unavailable("game.frametime", "Frame Time", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "PresentMon ETW", Name, SensorAvailability.PermissionRequired,
                reason, qualityNote));
            readings.Add(SensorReading.Unavailable("game.fps.1percent_low", "1% Low FPS", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "PresentMon ETW", Name, SensorAvailability.PermissionRequired,
                reason, qualityNote));
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        if (_targetPid == 0)
        {
            const string waiting = "Waiting for configured game process to start.";
            readings.Add(SensorReading.Unavailable("game.fps", "FPS", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.TemporarilyUnavailable, waiting, qualityNote));
            readings.Add(SensorReading.Unavailable("game.frametime", "Frame Time", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.TemporarilyUnavailable, waiting, qualityNote));
            readings.Add(SensorReading.Unavailable("game.fps.1percent_low", "1% Low FPS", SensorCategory.Gaming,
                "presentmon", "PresentMon ETW", Name, SensorAvailability.TemporarilyUnavailable, waiting, qualityNote));
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        bool isStale = now - _lastFrameTimestamp > TimeSpan.FromSeconds(4);
        if (_calculator.Count == 0 || isStale)
        {
            const string waitingFrames = "Waiting for DXGI/Present frames from target process (may be blocked by anti-cheat or exclusive fullscreen).";
            readings.Add(SensorReading.Unavailable("game.fps", "FPS", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "Game", Name, SensorAvailability.TemporarilyUnavailable, waitingFrames, qualityNote));
            readings.Add(SensorReading.Unavailable("game.frametime", "Frame Time", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "Game", Name, SensorAvailability.TemporarilyUnavailable, waitingFrames, qualityNote));
            readings.Add(SensorReading.Unavailable("game.fps.1percent_low", "1% Low FPS", SensorCategory.Gaming,
                "presentmon", _targetProcessName ?? "Game", Name, SensorAvailability.TemporarilyUnavailable, waitingFrames, qualityNote));
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
        }

        var hwName = _targetProcessName ?? $"PID {_targetPid}";
        readings.Add(new SensorReading
        {
            MetricId = "game.fps",
            DisplayName = "FPS",
            Value = Math.Round(_calculator.GetAverageFps(), 1),
            Unit = SensorUnit.Fps,
            Category = SensorCategory.Gaming,
            HardwareId = $"pid:{_targetPid}",
            HardwareName = hwName,
            ProviderName = Name,
            Timestamp = now,
            Availability = SensorAvailability.Available,
            SourceType = SensorSourceType.Measured,
            QualityNote = qualityNote
        });

        readings.Add(new SensorReading
        {
            MetricId = "game.frametime",
            DisplayName = "Frame Time",
            Value = Math.Round(_calculator.GetAverageFrameTime(), 2),
            Unit = SensorUnit.Milliseconds,
            Category = SensorCategory.Gaming,
            HardwareId = $"pid:{_targetPid}",
            HardwareName = hwName,
            ProviderName = Name,
            Timestamp = now,
            Availability = SensorAvailability.Available,
            SourceType = SensorSourceType.Measured,
            QualityNote = qualityNote
        });

        if (_calculator.Count >= 10)
        {
            readings.Add(new SensorReading
            {
                MetricId = "game.fps.1percent_low",
                DisplayName = "1% Low FPS",
                Value = Math.Round(_calculator.GetOnePercentLowFps(), 1),
                Unit = SensorUnit.Fps,
                Category = SensorCategory.Gaming,
                HardwareId = $"pid:{_targetPid}",
                HardwareName = hwName,
                ProviderName = Name,
                Timestamp = now,
                Availability = SensorAvailability.Available,
                SourceType = SensorSourceType.Calculated,
                QualityNote = qualityNote
            });
        }
        else
        {
            readings.Add(SensorReading.Unavailable("game.fps.1percent_low", "1% Low FPS", SensorCategory.Gaming,
                $"pid:{_targetPid}", hwName, Name, SensorAvailability.TemporarilyUnavailable,
                "Collecting frame samples for 1% low calculation.", qualityNote));
        }

        return Task.FromResult<IReadOnlyList<SensorReading>>(readings.AsReadOnly());
    }

    public IReadOnlyList<HardwareInfo> GetDetectedHardware()
    {
        return new[]
        {
            new HardwareInfo("presentmon", "ETW / PresentMon Frame Telemetry", "Windows DXGI / Intel PresentMon",
                null, SensorCategory.Gaming,
                _presentMonExePath is not null ? $"CLI: {_presentMonExePath}" : "In-process TraceEvent DXGI ETW")
        };
    }

    public ProviderHealthInfo GetHealthInfo() => new(
        Name,
        Health,
        HealthReason,
        DateTimeOffset.UtcNow,
        _calculator.Count > 0 ? 3 : 0,
        IsAvailable);

    private void RestartCapture()
    {
        StopCapture();
        uint pid = _targetPid;
        if (pid == 0 || !_captureEnabled) return;

        _captureCts = new CancellationTokenSource();
        var ct = _captureCts.Token;

        if (!string.IsNullOrEmpty(_presentMonExePath) && File.Exists(_presentMonExePath))
        {
            _captureTask = Task.Run(() => RunPresentMonCliAsync(_presentMonExePath, pid, ct), ct);
        }
        else
        {
            _captureTask = Task.Run(() => RunDxgiEtwSession(pid, ct), ct);
        }
    }

    private void StopCapture()
    {
        try { _captureCts?.Cancel(); } catch { }
        try
        {
            if (_presentMonProcess is not null && !_presentMonProcess.HasExited)
                _presentMonProcess.Kill(entireProcessTree: true);
        }
        catch { }
        try { _etwSession?.Stop(); } catch { }
        try { _etwSession?.Dispose(); } catch { }
        _etwSession = null;
        _presentMonProcess?.Dispose();
        _presentMonProcess = null;
        try { _captureTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;
    }

    private async Task RunPresentMonCliAsync(string exePath, uint pid, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--process_id {pid} --output_stdout --stop_existing_session",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _presentMonProcess = Process.Start(psi);
            if (_presentMonProcess is null) return;

            var headerLine = await _presentMonProcess.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(headerLine)) return;

            var headers = headerLine.Split(',');
            int frameTimeIdx = Array.FindIndex(headers, h =>
                h.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("FrameTime", StringComparison.OrdinalIgnoreCase));

            if (frameTimeIdx < 0) return;

            while (!ct.IsCancellationRequested && !_presentMonProcess.HasExited)
            {
                var line = await _presentMonProcess.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                var cols = line.Split(',');
                if (frameTimeIdx < cols.Length &&
                    double.TryParse(cols[frameTimeIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) &&
                    ms > 0 && ms < 5000)
                {
                    _calculator.AddFrameTime(ms);
                    _lastFrameTimestamp = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Health = ProviderHealth.Degraded;
            HealthReason = $"PresentMon CLI capture stopped: {ex.Message}";
        }
    }

    private void RunDxgiEtwSession(uint pid, CancellationToken ct)
    {
        try
        {
            var sessionName = $"CarbonMonitor_DXGI_{Environment.ProcessId}";
            _etwSession = new TraceEventSession(sessionName);
            using var reg = ct.Register(() =>
            {
                try { _etwSession?.Stop(); } catch { }
            });

            _etwSession.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational);

            double lastTimeMs = 0;
            _etwSession.Source.Dynamic.All += data =>
            {
                if (ct.IsCancellationRequested) return;
                if ((uint)data.ProcessID != pid) return;

                // Event ID 42 is DXGI Present_Start on Windows 10/11
                if ((int)data.ID == 42 || data.EventName.Contains("Present", StringComparison.OrdinalIgnoreCase))
                {
                    double nowMs = data.TimeStampRelativeMSec;
                    if (lastTimeMs > 0)
                    {
                        double delta = nowMs - lastTimeMs;
                        if (delta > 0.2 && delta < 5000)
                        {
                            _calculator.AddFrameTime(delta);
                            _lastFrameTimestamp = DateTimeOffset.UtcNow;
                        }
                    }
                    lastTimeMs = nowMs;
                }
            };

            Health = ProviderHealth.Healthy;
            HealthReason = $"Listening to DXGI Present ETW events for PID {pid}";
            _etwSession.Source.Process();
        }
        catch (UnauthorizedAccessException)
        {
            _permissionDenied = true;
            Health = ProviderHealth.Degraded;
            HealthReason = "ETW real-time session requires Administrator rights or Performance Log Users membership (or place PresentMon.exe alongside app).";
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                Health = ProviderHealth.Degraded;
                HealthReason = $"ETW capture unavailable: {ex.Message}";
            }
        }
    }

    private static string? LocatePresentMonExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "PresentMon.exe"),
            Path.Combine(baseDir, "tools", "PresentMon.exe"),
            Path.Combine(baseDir, "PresentMon-x64.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        StopCapture();
    }
}
