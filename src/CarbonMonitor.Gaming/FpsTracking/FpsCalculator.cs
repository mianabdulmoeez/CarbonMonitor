namespace CarbonMonitor.Gaming.FpsTracking;

using System.Collections.Concurrent;

/// <summary>
/// Calculates FPS, latest frame time, and 1% low FPS statistics from frame durations.
/// </summary>
public class FpsCalculator
{
    private readonly int _bufferSize;
    private readonly ConcurrentQueue<double> _frameTimes = new();
    private int _count;
    private double _latestFrameTime;

    public FpsCalculator(int bufferSize = 300)
    {
        _bufferSize = Math.Max(1, bufferSize);
    }

    /// <summary>Add a frame time in milliseconds.</summary>
    public void AddFrameTime(double frameTimeMs)
    {
        if (double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs) || frameTimeMs < 0)
            return;

        _latestFrameTime = frameTimeMs;
        _frameTimes.Enqueue(frameTimeMs);
        Interlocked.Increment(ref _count);

        while (_count > _bufferSize && _frameTimes.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }

    /// <summary>Get the average FPS across buffered frames.</summary>
    public double GetAverageFps()
    {
        var times = _frameTimes.ToArray();
        if (times.Length == 0) return 0;

        var avgFrameTimeMs = times.Average();
        if (avgFrameTimeMs <= 0) return 0;

        return 1000.0 / avgFrameTimeMs;
    }

    /// <summary>Get instantaneous FPS from the latest frame duration.</summary>
    public double GetCurrentFps()
    {
        var latest = _latestFrameTime;
        return latest > 0 ? 1000.0 / latest : 0;
    }

    /// <summary>Get the latest single frame time in milliseconds.</summary>
    public double GetLatestFrameTime() => _latestFrameTime;

    /// <summary>Get the 1% low FPS (slowest 1% frame times).</summary>
    public double GetOnePercentLowFps()
    {
        var times = _frameTimes.ToArray();
        if (times.Length == 0) return 0;

        var sorted = times.OrderByDescending(t => t).ToArray();
        var count = Math.Max(1, (int)Math.Ceiling(sorted.Length * 0.01));
        var avgWorstMs = sorted.Take(count).Average();

        if (avgWorstMs <= 0) return 0;
        return 1000.0 / avgWorstMs;
    }

    /// <summary>Get the average frame time in milliseconds.</summary>
    public double GetAverageFrameTime()
    {
        var times = _frameTimes.ToArray();
        if (times.Length == 0) return 0;
        return times.Average();
    }

    /// <summary>Number of frame times in the buffer.</summary>
    public int Count => _count;

    /// <summary>Reset all frame time data.</summary>
    public void Reset()
    {
        while (_frameTimes.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _count, 0);
        _latestFrameTime = 0;
    }
}
