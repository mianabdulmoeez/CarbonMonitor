namespace CarbonMonitor.Gaming.ProcessDetection;

using System.Diagnostics;

/// <summary>
/// Detects whether any configured game process is currently running without reading game memory or injecting code.
/// </summary>
public class GameProcessDetector : IDisposable
{
    private readonly HashSet<string> _gameProcessNames;
    private readonly TimeSpan _cacheDuration;
    private readonly object _lock = new();
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _lastResult;
    private string? _lastDetectedProcess;
    private int _lastDetectedPid;

    public GameProcessDetector(IEnumerable<string> gameProcessNames, TimeSpan? cacheDuration = null)
    {
        _gameProcessNames = new HashSet<string>(
            (gameProcessNames ?? Enumerable.Empty<string>()).Select(NormalizeName).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>The last detected game process name, if any.</summary>
    public string? LastDetectedProcess
    {
        get { lock (_lock) { return _lastDetectedProcess; } }
    }

    /// <summary>The last detected game process ID, or 0 if none.</summary>
    public int LastDetectedPid
    {
        get { lock (_lock) { return _lastDetectedPid; } }
    }

    /// <summary>Check if any configured game process is running.</summary>
    public bool IsGameRunning()
    {
        lock (_lock)
        {
            if (_gameProcessNames.Count == 0)
            {
                _lastResult = false;
                _lastDetectedProcess = null;
                _lastDetectedPid = 0;
                return false;
            }

            var now = DateTime.UtcNow;
            if (now - _lastCheck < _cacheDuration)
                return _lastResult;

            _lastCheck = now;
            _lastResult = false;
            _lastDetectedProcess = null;
            _lastDetectedPid = 0;

            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (!_lastResult && _gameProcessNames.Contains(proc.ProcessName))
                        {
                            _lastResult = true;
                            _lastDetectedProcess = proc.ProcessName;
                            _lastDetectedPid = proc.Id;
                        }
                    }
                    catch
                    {
                        // Some protected processes may throw on property access
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
                _lastResult = false;
            }

            return _lastResult;
        }
    }

    /// <summary>Returns whether a specific process name is currently running.</summary>
    public bool IsProcessRunning(string processName)
    {
        var normalized = NormalizeName(processName);
        if (string.IsNullOrEmpty(normalized)) return false;

        try
        {
            var procs = Process.GetProcessesByName(normalized);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Check if a specific process name is in the configured list.</summary>
    public bool IsConfiguredGame(string processName)
    {
        lock (_lock) { return _gameProcessNames.Contains(NormalizeName(processName)); }
    }

    /// <summary>Add a process name to watch.</summary>
    public void AddGameProcess(string processName)
    {
        var normalized = NormalizeName(processName);
        if (string.IsNullOrEmpty(normalized)) return;
        lock (_lock)
        {
            _gameProcessNames.Add(normalized);
            _lastCheck = DateTime.MinValue;
        }
    }

    /// <summary>Remove a process name from the watch list.</summary>
    public void RemoveGameProcess(string processName)
    {
        lock (_lock)
        {
            _gameProcessNames.Remove(NormalizeName(processName));
            _lastCheck = DateTime.MinValue;
        }
    }

    /// <summary>Replace the configured watch list.</summary>
    public void UpdateWatchList(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            _gameProcessNames.Clear();
            foreach (var name in processNames ?? Enumerable.Empty<string>())
            {
                var norm = NormalizeName(name);
                if (norm.Length > 0)
                    _gameProcessNames.Add(norm);
            }
            _lastCheck = DateTime.MinValue;
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _gameProcessNames.Clear();
        }
    }
}
