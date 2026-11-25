namespace CarbonMonitor.Providers.Tests.Gaming;

using CarbonMonitor.Gaming.FpsTracking;
using Xunit;

public class FpsCalculatorTests
{
    // ── Empty buffer ──

    [Fact]
    public void GetAverageFps_EmptyBuffer_ReturnsZero()
    {
        var calc = new FpsCalculator();
        Assert.Equal(0, calc.GetAverageFps());
    }

    [Fact]
    public void GetOnePercentLowFps_EmptyBuffer_ReturnsZero()
    {
        var calc = new FpsCalculator();
        Assert.Equal(0, calc.GetOnePercentLowFps());
    }

    [Fact]
    public void GetAverageFrameTime_EmptyBuffer_ReturnsZero()
    {
        var calc = new FpsCalculator();
        Assert.Equal(0, calc.GetAverageFrameTime());
    }

    [Fact]
    public void Count_EmptyBuffer_IsZero()
    {
        var calc = new FpsCalculator();
        Assert.Equal(0, calc.Count);
    }

    // ── Single frame time ──

    [Fact]
    public void GetAverageFps_SingleFrame_16ms_Returns60Fps()
    {
        var calc = new FpsCalculator();
        calc.AddFrameTime(16.6667); // ~60 FPS

        var fps = calc.GetAverageFps();
        Assert.InRange(fps, 59.0, 61.0);
    }

    [Fact]
    public void GetAverageFps_SingleFrame_6_94ms_Returns144Fps()
    {
        var calc = new FpsCalculator();
        calc.AddFrameTime(1000.0 / 144.0); // ~6.944ms for 144 FPS

        var fps = calc.GetAverageFps();
        Assert.InRange(fps, 143.0, 145.0);
    }

    [Fact]
    public void GetAverageFps_SingleFrame_33ms_Returns30Fps()
    {
        var calc = new FpsCalculator();
        calc.AddFrameTime(33.3333); // ~30 FPS

        var fps = calc.GetAverageFps();
        Assert.InRange(fps, 29.0, 31.0);
    }

    // ── 1% low calculation with known data ──

    [Fact]
    public void GetOnePercentLow_KnownData_ReturnsExpected()
    {
        var calc = new FpsCalculator(bufferSize: 500);

        // Add 100 frames: 99 at 16.67ms (60fps) and 1 stutter at 100ms (10fps)
        for (int i = 0; i < 99; i++)
            calc.AddFrameTime(16.6667);
        calc.AddFrameTime(100.0); // 10 FPS stutter

        var onePercentLow = calc.GetOnePercentLowFps();

        // 1% of 100 = 1 frame, the worst frame at 100ms = 10fps
        Assert.InRange(onePercentLow, 9.0, 11.0);
    }

    [Fact]
    public void GetOnePercentLow_AllSameFrameTime_EqualToAverage()
    {
        var calc = new FpsCalculator();

        for (int i = 0; i < 100; i++)
            calc.AddFrameTime(16.6667);

        var avg = calc.GetAverageFps();
        var low = calc.GetOnePercentLowFps();

        // With uniform frame times, 1% low should equal average
        Assert.InRange(low, avg - 1.0, avg + 1.0);
    }

    // ── Buffer overflow (circular buffer) ──

    [Fact]
    public void BufferOverflow_OldDataDiscarded()
    {
        var bufferSize = 10;
        var calc = new FpsCalculator(bufferSize: bufferSize);

        // Fill beyond buffer
        for (int i = 0; i < 20; i++)
            calc.AddFrameTime(16.6667);

        Assert.Equal(bufferSize, calc.Count);
    }

    [Fact]
    public void BufferOverflow_RecentDataRetained()
    {
        var calc = new FpsCalculator(bufferSize: 5);

        // Add 5 frames at 10ms, then 5 frames at 100ms
        for (int i = 0; i < 5; i++)
            calc.AddFrameTime(10.0);
        for (int i = 0; i < 5; i++)
            calc.AddFrameTime(100.0);

        // Buffer should now contain only the 100ms frames
        var avgFrameTime = calc.GetAverageFrameTime();
        Assert.InRange(avgFrameTime, 95.0, 105.0); // Should be ~100ms
    }

    // ── Reset ──

    [Fact]
    public void Reset_ClearsAllData()
    {
        var calc = new FpsCalculator();

        for (int i = 0; i < 50; i++)
            calc.AddFrameTime(16.6667);

        Assert.True(calc.Count > 0);

        calc.Reset();

        Assert.Equal(0, calc.Count);
        Assert.Equal(0, calc.GetAverageFps());
        Assert.Equal(0, calc.GetOnePercentLowFps());
        Assert.Equal(0, calc.GetAverageFrameTime());
    }

    [Fact]
    public void Reset_CanAddDataAfterReset()
    {
        var calc = new FpsCalculator();

        calc.AddFrameTime(16.6667);
        calc.Reset();
        calc.AddFrameTime(33.3333);

        Assert.Equal(1, calc.Count);
        var fps = calc.GetAverageFps();
        Assert.InRange(fps, 29.0, 31.0);
    }

    // ── Thread safety ──

    [Fact]
    public void ThreadSafety_ParallelAdds_NoCrash()
    {
        var calc = new FpsCalculator(bufferSize: 1000);

        Parallel.For(0, 500, i =>
        {
            calc.AddFrameTime(16.6667 + (i % 10));
        });

        // Should not crash and count should be reasonable
        Assert.True(calc.Count > 0);
        Assert.True(calc.Count <= 1000);
    }

    [Fact]
    public void ThreadSafety_ParallelReadsAndWrites_NoCrash()
    {
        var calc = new FpsCalculator(bufferSize: 100);
        var errors = new List<Exception>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                if (i % 2 == 0)
                    calc.AddFrameTime(16.6667);
                else
                {
                    _ = calc.GetAverageFps();
                    _ = calc.GetOnePercentLowFps();
                    _ = calc.GetAverageFrameTime();
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        });

        Assert.Empty(errors);
    }

    // ── Edge cases ──

    [Fact]
    public void AddFrameTime_Zero_HandledGracefully()
    {
        var calc = new FpsCalculator();
        calc.AddFrameTime(0.0);

        // 0ms frame time → infinite FPS; implementation returns 0 since avg <= 0
        var fps = calc.GetAverageFps();
        // Either 0 or not throwing is acceptable
        Assert.True(fps >= 0 || double.IsPositiveInfinity(fps));
    }

    [Fact]
    public void AddFrameTime_VeryLarge_HandledGracefully()
    {
        var calc = new FpsCalculator();
        calc.AddFrameTime(1000000.0); // 1000 seconds per frame

        var fps = calc.GetAverageFps();
        Assert.True(fps < 1.0); // Should be very low FPS
    }

    [Fact]
    public void GetAverageFrameTime_ConsistentFrameTimes()
    {
        var calc = new FpsCalculator();

        for (int i = 0; i < 100; i++)
            calc.AddFrameTime(16.6667);

        var avgFrameTime = calc.GetAverageFrameTime();
        Assert.InRange(avgFrameTime, 16.0, 17.0);
    }
}
