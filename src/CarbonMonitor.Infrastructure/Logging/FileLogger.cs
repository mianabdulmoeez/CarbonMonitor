using System.Collections.Concurrent;
using System.Globalization;

namespace CarbonMonitor.Infrastructure.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Lightweight bounded rolling local file logger under %LocalAppData%\CarbonMonitor\logs.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private static readonly Lazy<FileLogger> _instance = new(() => new FileLogger());
    public static FileLogger Instance => _instance.Value;

    private readonly string _logDirectory;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushTask;
    private readonly int _maxQueueSize = 5000;
    private int _retentionDays;
    private volatile bool _disposed;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public string LogDirectory => _logDirectory;

    public FileLogger(int retentionDays = 7, string? customLogDirectory = null)
    {
        _retentionDays = Math.Clamp(retentionDays, 1, 365);
        _logDirectory = customLogDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CarbonMonitor", "logs");

        try
        {
            Directory.CreateDirectory(_logDirectory);
            CleanupOldLogs();
        }
        catch
        {
            // Non-fatal if log directory cannot be created
        }

        _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
    }

    public void Configure(int retentionDays, bool verboseLogging)
    {
        _retentionDays = Math.Clamp(retentionDays, 1, 365);
        MinimumLevel = verboseLogging ? LogLevel.Debug : LogLevel.Info;
        CleanupOldLogs();
    }

    public void Debug(string source, string message) => Enqueue(LogLevel.Debug, source, message);
    public void Info(string source, string message) => Enqueue(LogLevel.Info, source, message);
    public void Warning(string source, string message) => Enqueue(LogLevel.Warning, source, message);
    public void Error(string source, string message) => Enqueue(LogLevel.Error, source, message);

    private void Enqueue(LogLevel level, string source, string message)
    {
        if (level < MinimumLevel || _disposed) return;

        while (_queue.Count >= _maxQueueSize)
            _queue.TryDequeue(out _);

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        _queue.Enqueue($"[{timestamp}] [{level}] [{source}] {message}");
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
                await FlushAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Do not crash application on logging failure
            }
        }

        await FlushAsync().ConfigureAwait(false);
    }

    public async Task FlushAsync()
    {
        if (_queue.IsEmpty) return;

        var lines = new List<string>();
        while (_queue.TryDequeue(out var line))
            lines.Add(line);

        if (lines.Count == 0) return;

        var fileName = $"CarbonMonitor-{DateTime.Now:yyyy-MM-dd}.log";
        var filePath = Path.Combine(_logDirectory, fileName);

        try
        {
            Directory.CreateDirectory(_logDirectory);
            await File.AppendAllLinesAsync(filePath, lines).ConfigureAwait(false);
        }
        catch
        {
            // Swallow file I/O issues gracefully
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logDirectory)) return;
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.GetFiles(_logDirectory, "CarbonMonitor-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _flushTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
