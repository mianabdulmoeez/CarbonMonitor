namespace CarbonMonitor.Gaming.ProcessDetection;

/// <summary>
/// Lightweight polling watcher that notifies subscribers when a configured game starts or exits.
/// </summary>
public sealed class GameProcessWatcher : IDisposable
{
    private readonly GameProcessDetector _detector;
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private string? _activeProcess;
    private int _activePid;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);
    public bool IsAnyGameRunning => _activeProcess is not null;
    public string? ActiveProcessName => _activeProcess;
    public int ActiveProcessId => _activePid;

    public event Action<string, int>? OnGameStarted;
    public event Action<string>? OnGameStopped;

    public GameProcessWatcher(GameProcessDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public void Start()
    {
        if (_watchTask is not null) return;
        _cts = new CancellationTokenSource();
        _watchTask = WatchLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _watchTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _cts = null;
        _watchTask = null;
        _activeProcess = null;
        _activePid = 0;
    }

    public void UpdateWatchList(IEnumerable<string> processNames)
    {
        _detector.UpdateWatchList(processNames);
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool running = _detector.IsGameRunning();
                if (running)
                {
                    var name = _detector.LastDetectedProcess;
                    var pid = _detector.LastDetectedPid;
                    if (!string.IsNullOrEmpty(name) &&
                        (!string.Equals(_activeProcess, name, StringComparison.OrdinalIgnoreCase) || _activePid != pid))
                    {
                        _activeProcess = name;
                        _activePid = pid;
                        OnGameStarted?.Invoke(name, pid);
                    }
                }
                else if (_activeProcess is not null)
                {
                    var stopped = _activeProcess;
                    _activeProcess = null;
                    _activePid = 0;
                    OnGameStopped?.Invoke(stopped);
                }

                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep watcher resilient
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
