using System.Drawing;
using System.Drawing.Drawing2D;

namespace CarbonMonitor.App.Controls;

/// <summary>
/// Lightweight double-buffered rolling history sparkline graph.
/// Stores up to 60 samples in a fixed circular buffer and skips painting when hidden.
/// </summary>
public sealed class SparklineChart : Control
{
    private readonly double[] _samples = new double[60];
    private int _head;
    private int _count;

    private string _title = "History";
    private string _unitSuffix = "%";
    private double _minValue = 0;
    private double _maxValue = 100;
    private bool _autoScaleMax;
    private Color _lineColor = Color.FromArgb(56, 189, 248);
    private Color _bgColor = Color.FromArgb(18, 22, 31);
    private Color _gridColor = Color.FromArgb(34, 42, 56);
    private Color _textColor = Color.FromArgb(156, 167, 184);

    public SparklineChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 68;
        MinimumSize = new Size(120, 52);
    }

    public void Configure(string title, string unitSuffix, double minValue, double maxValue, bool autoScaleMax = false)
    {
        _title = title;
        _unitSuffix = unitSuffix;
        _minValue = minValue;
        _maxValue = maxValue;
        _autoScaleMax = autoScaleMax;
    }

    public void ApplyTheme(Color lineColor, DashboardThemePalette palette)
    {
        _lineColor = lineColor;
        _bgColor = palette.TileBackground;
        _gridColor = palette.GridLineColor;
        _textColor = palette.SecondaryText;
        if (Visible) Invalidate();
    }

    public void AddSample(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _samples[_head] = value;
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;

        if (Visible && FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized })
        {
            Invalidate();
        }
    }

    public void Clear()
    {
        Array.Clear(_samples);
        _head = 0;
        _count = 0;
        if (Visible) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        using (var bgBrush = new SolidBrush(_bgColor))
            g.FillRectangle(bgBrush, rect);

        using (var borderPen = new Pen(_gridColor, 1f))
            g.DrawRectangle(borderPen, 0, 0, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));

        // Draw subtle horizontal grid lines
        int topPad = 18;
        int bottomPad = 6;
        int plotH = Math.Max(10, rect.Height - topPad - bottomPad);
        int plotW = Math.Max(10, rect.Width - 8);

        using (var gridPen = new Pen(_gridColor, 1f) { DashStyle = DashStyle.Dot })
        {
            g.DrawLine(gridPen, 4, topPad, rect.Width - 4, topPad);
            g.DrawLine(gridPen, 4, topPad + plotH / 2, rect.Width - 4, topPad + plotH / 2);
            g.DrawLine(gridPen, 4, topPad + plotH, rect.Width - 4, topPad + plotH);
        }

        double currentVal = 0;
        double peakVal = 0;

        if (_count > 0)
        {
            var ordered = new double[_count];
            int start = (_head - _count + _samples.Length) % _samples.Length;
            for (int i = 0; i < _count; i++)
            {
                ordered[i] = _samples[(start + i) % _samples.Length];
            }

            currentVal = ordered[^1];
            peakVal = ordered.Max();

            double effectiveMax = _autoScaleMax
                ? Math.Max(_maxValue, Math.Ceiling(peakVal * 1.15))
                : _maxValue;
            if (effectiveMax <= _minValue) effectiveMax = _minValue + 1;

            if (_count >= 2)
            {
                var points = new PointF[_count];
                float dx = (float)plotW / (_samples.Length - 1);
                float startX = 4f + (_samples.Length - _count) * dx;

                for (int i = 0; i < _count; i++)
                {
                    double norm = Math.Clamp((ordered[i] - _minValue) / (effectiveMax - _minValue), 0.0, 1.0);
                    float x = startX + i * dx;
                    float y = topPad + (float)((1.0 - norm) * plotH);
                    points[i] = new PointF(x, y);
                }

                using var areaPath = new GraphicsPath();
                areaPath.AddLines(points);
                areaPath.AddLine(points[^1].X, topPad + plotH, points[0].X, topPad + plotH);
                areaPath.CloseFigure();

                using (var fillBrush = new SolidBrush(Color.FromArgb(45, _lineColor)))
                    g.FillPath(fillBrush, areaPath);

                using (var linePen = new Pen(_lineColor, 1.8f))
                    g.DrawLines(linePen, points);
            }
        }

        // Header text: title on left, latest/peak on right
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        using var textBrush = new SolidBrush(_textColor);
        g.DrawString(_title, font, textBrush, 6, 2);

        string stats = _count > 0
            ? $"{currentVal:F1}{_unitSuffix}  (Peak {peakVal:F1}{_unitSuffix})"
            : "Waiting...";
        var size = g.MeasureString(stats, font);
        g.DrawString(stats, font, textBrush, Math.Max(6, rect.Width - size.Width - 6), 2);
    }
}
