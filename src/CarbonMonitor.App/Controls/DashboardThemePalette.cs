using System.Drawing;

namespace CarbonMonitor.App.Controls;

public sealed record DashboardThemePalette(
    string Name,
    Color WindowBackground,
    Color HeaderBackground,
    Color CardBackground,
    Color CardBorder,
    Color TileBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color MutedText,
    Color CpuAccent,
    Color GpuAccent,
    Color MemoryAccent,
    Color StorageAccent,
    Color GamingAccent,
    Color OkColor,
    Color WarningColor,
    Color CriticalColor,
    Color BarTrackColor,
    Color GridLineColor)
{
    public static DashboardThemePalette DarkGaming { get; } = new(
        Name: "DarkGaming",
        WindowBackground: Color.FromArgb(14, 17, 23),
        HeaderBackground: Color.FromArgb(20, 25, 34),
        CardBackground: Color.FromArgb(22, 27, 37),
        CardBorder: Color.FromArgb(40, 49, 66),
        TileBackground: Color.FromArgb(27, 33, 46),
        PrimaryText: Color.FromArgb(236, 241, 248),
        SecondaryText: Color.FromArgb(156, 167, 184),
        MutedText: Color.FromArgb(112, 124, 142),
        CpuAccent: Color.FromArgb(56, 189, 248),     // Cyan
        GpuAccent: Color.FromArgb(46, 204, 113),     // Emerald
        MemoryAccent: Color.FromArgb(167, 139, 250), // Violet
        StorageAccent: Color.FromArgb(251, 146, 60), // Amber/Orange
        GamingAccent: Color.FromArgb(244, 63, 94),   // Rose
        OkColor: Color.FromArgb(46, 204, 113),
        WarningColor: Color.FromArgb(245, 158, 11),
        CriticalColor: Color.FromArgb(239, 68, 68),
        BarTrackColor: Color.FromArgb(35, 43, 58),
        GridLineColor: Color.FromArgb(34, 42, 56));

    public static DashboardThemePalette HighContrast { get; } = new(
        Name: "HighContrast",
        WindowBackground: Color.Black,
        HeaderBackground: Color.FromArgb(12, 12, 12),
        CardBackground: Color.FromArgb(10, 10, 10),
        CardBorder: Color.White,
        TileBackground: Color.FromArgb(18, 18, 18),
        PrimaryText: Color.White,
        SecondaryText: Color.FromArgb(225, 225, 225),
        MutedText: Color.FromArgb(190, 190, 190),
        CpuAccent: Color.Cyan,
        GpuAccent: Color.Lime,
        MemoryAccent: Color.Yellow,
        StorageAccent: Color.Orange,
        GamingAccent: Color.Magenta,
        OkColor: Color.Lime,
        WarningColor: Color.Yellow,
        CriticalColor: Color.Red,
        BarTrackColor: Color.FromArgb(45, 45, 45),
        GridLineColor: Color.FromArgb(60, 60, 60));

    public static DashboardThemePalette Light { get; } = new(
        Name: "Light",
        WindowBackground: Color.FromArgb(241, 244, 249),
        HeaderBackground: Color.White,
        CardBackground: Color.White,
        CardBorder: Color.FromArgb(214, 220, 230),
        TileBackground: Color.FromArgb(247, 249, 252),
        PrimaryText: Color.FromArgb(24, 30, 41),
        SecondaryText: Color.FromArgb(75, 85, 99),
        MutedText: Color.FromArgb(115, 125, 140),
        CpuAccent: Color.FromArgb(2, 132, 199),
        GpuAccent: Color.FromArgb(22, 163, 74),
        MemoryAccent: Color.FromArgb(124, 58, 237),
        StorageAccent: Color.FromArgb(217, 119, 6),
        GamingAccent: Color.FromArgb(225, 29, 72),
        OkColor: Color.FromArgb(22, 163, 74),
        WarningColor: Color.FromArgb(217, 119, 6),
        CriticalColor: Color.FromArgb(220, 38, 38),
        BarTrackColor: Color.FromArgb(226, 232, 240),
        GridLineColor: Color.FromArgb(232, 236, 242));

    public static DashboardThemePalette FromName(string? name) => name switch
    {
        "HighContrast" => HighContrast,
        "Light" => Light,
        _ => DarkGaming
    };
}
