using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CarbonMonitor.App.Controls;

/// <summary>
/// Sleek custom-painted progress bar with threshold coloring (normal, warning, critical).
/// </summary>
public sealed class MetricBarControl : Control
{
    private double _percentage;
    private double _warningAt = 85;
    private double _criticalAt = 95;
    private Color _trackColor = Color.FromArgb(35, 43, 58);
    private Color _accentColor = Color.FromArgb(56, 189, 248);
    private Color _warningColor = Color.FromArgb(245, 158, 11);
    private Color _criticalColor = Color.FromArgb(239, 68, 68);

    public MetricBarControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 10;
        MinimumSize = new Size(60, 8);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Percentage
    {
        get => _percentage;
        set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_percentage - clamped) < 0.05) return;
            _percentage = clamped;
            if (Visible) Invalidate();
        }
    }

    public void SetThresholds(double warning, double critical)
    {
        _warningAt = warning;
        _criticalAt = critical;
    }

    public void ApplyTheme(Color accentColor, DashboardThemePalette palette)
    {
        _accentColor = accentColor;
        _trackColor = palette.BarTrackColor;
        _warningColor = palette.WarningColor;
        _criticalColor = palette.CriticalColor;
        if (Visible) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
        using (var trackBrush = new SolidBrush(_trackColor))
        using (var path = CreateRoundedRect(rect, Math.Min(4, Height / 2)))
        {
            g.FillPath(trackBrush, path);
        }

        if (_percentage > 0.2)
        {
            int fillW = Math.Max(4, (int)Math.Round(rect.Width * (_percentage / 100.0)));
            var fillRect = new Rectangle(0, 0, Math.Min(rect.Width, fillW), rect.Height);
            Color barColor = _percentage >= _criticalAt ? _criticalColor :
                             _percentage >= _warningAt ? _warningColor : _accentColor;

            using var fillBrush = new SolidBrush(barColor);
            using var fillPath = CreateRoundedRect(fillRect, Math.Min(4, Height / 2));
            g.FillPath(fillBrush, fillPath);
        }
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
