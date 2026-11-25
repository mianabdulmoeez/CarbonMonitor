namespace CarbonMonitor.Providers.Tests.Gaming;

using CarbonMonitor.Gaming.ProcessDetection;
using Xunit;

public class GameProcessDetectorTests
{
    // ── Unknown process ──

    [Fact]
    public void IsConfiguredGame_UnknownProcess_ReturnsFalse()
    {
        var detector = new GameProcessDetector(new[] { "VALORANT-Win64-Shipping" });
        Assert.False(detector.IsConfiguredGame("notepad"));
    }

    [Fact]
    public void IsConfiguredGame_KnownProcess_ReturnsTrue()
    {
        var detector = new GameProcessDetector(new[] { "VALORANT-Win64-Shipping", "cs2" });
        Assert.True(detector.IsConfiguredGame("VALORANT-Win64-Shipping"));
        Assert.True(detector.IsConfiguredGame("cs2"));
    }

    [Fact]
    public void IsConfiguredGame_CaseInsensitive()
    {
        var detector = new GameProcessDetector(new[] { "VALORANT-Win64-Shipping" });
        Assert.True(detector.IsConfiguredGame("valorant-win64-shipping"));
        Assert.True(detector.IsConfiguredGame("Valorant-Win64-Shipping"));
    }

    // ── Empty process list ──

    [Fact]
    public void EmptyProcessList_IsGameRunning_ReturnsFalse()
    {
        var detector = new GameProcessDetector(Enumerable.Empty<string>());
        Assert.False(detector.IsGameRunning());
    }

    [Fact]
    public void EmptyProcessList_IsConfiguredGame_ReturnsFalse()
    {
        var detector = new GameProcessDetector(Array.Empty<string>());
        Assert.False(detector.IsConfiguredGame("any_process"));
    }

    // ── Cache staleness ──

    [Fact]
    public void Cache_MultipleCallsWithinDuration_UsesCache()
    {
        var detector = new GameProcessDetector(
            new[] { "NonExistentProcess12345" },
            cacheDuration: TimeSpan.FromSeconds(30));

        // First call populates cache
        var first = detector.IsGameRunning();
        // Second call should use cached value
        var second = detector.IsGameRunning();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Cache_ExpiredCache_RefreshesResult()
    {
        var detector = new GameProcessDetector(
            new[] { "NonExistentProcess12345" },
            cacheDuration: TimeSpan.FromMilliseconds(1));

        detector.IsGameRunning(); // First call
        System.Threading.Thread.Sleep(10); // Wait for cache to expire
        var result = detector.IsGameRunning(); // Should re-check

        Assert.False(result); // Process doesn't exist
    }

    // ── Add/Remove game processes ──

    [Fact]
    public void AddGameProcess_NewProcessDetectable()
    {
        var detector = new GameProcessDetector(Array.Empty<string>());
        Assert.False(detector.IsConfiguredGame("cs2"));

        detector.AddGameProcess("cs2");
        Assert.True(detector.IsConfiguredGame("cs2"));
    }

    [Fact]
    public void RemoveGameProcess_ProcessNoLongerDetectable()
    {
        var detector = new GameProcessDetector(new[] { "cs2" });
        Assert.True(detector.IsConfiguredGame("cs2"));

        detector.RemoveGameProcess("cs2");
        Assert.False(detector.IsConfiguredGame("cs2"));
    }

    // ── LastDetectedProcess ──

    [Fact]
    public void LastDetectedProcess_InitiallyNull()
    {
        var detector = new GameProcessDetector(new[] { "VALORANT-Win64-Shipping" });
        Assert.Null(detector.LastDetectedProcess);
    }

    [Fact]
    public void LastDetectedProcess_NullWhenNoGameFound()
    {
        var detector = new GameProcessDetector(
            new[] { "NonExistentGameProcess99999" },
            cacheDuration: TimeSpan.Zero);

        detector.IsGameRunning();
        Assert.Null(detector.LastDetectedProcess);
    }

    // ── IsGameRunning with non-existent processes ──

    [Fact]
    public void IsGameRunning_NonExistentProcesses_ReturnsFalse()
    {
        var detector = new GameProcessDetector(
            new[] { "ThisProcessDoesNotExist123", "NeitherDoesThis456" },
            cacheDuration: TimeSpan.Zero);

        Assert.False(detector.IsGameRunning());
    }

    // ── Null-safe construction ──

    [Fact]
    public void Constructor_NullProcessNames_HandledGracefully()
    {
        var detector = new GameProcessDetector(null!);
        Assert.False(detector.IsGameRunning());
    }
}
