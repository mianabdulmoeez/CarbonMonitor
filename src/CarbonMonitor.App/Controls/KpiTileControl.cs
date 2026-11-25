using System.Drawing;

namespace CarbonMonitor.App.Controls;

/// <summary>
/// Compact overview KPI tile displayed across the top System Overview strip.
/// </summary>
public sealed class KpiTileControl : Panel
{
    private readonly Label _lblTitle;
    private readonly Label _lblValue;
    private readonly Label _lblSub;
    private readonly Panel _stripe;
    private Color _borderColor = Color.FromArgb(40, 49, 66);

    public KpiTileControl(string title)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(4);
        Padding = new Padding(10, 7, 10, 7);
        MinimumSize = new Size(135, 76);
        Height = 78;
        BackColor = Color.FromArgb(27, 33, 46);

        _stripe = new Panel
        {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = Color.FromArgb(56, 189, 248)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 0, 0, 0)
        };
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _lblTitle = new Label
        {
            Text = title.ToUpperInvariant(),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 167, 184),
            Margin = new Padding(0)
        };

        _lblValue = new Label
        {
            Text = "--",
            AutoSize = true,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.FromArgb(236, 241, 248),
            Margin = new Padding(0, 1, 0, 0)
        };

        _lblSub = new Label
        {
            Text = "--",
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.FromArgb(112, 124, 142),
            Margin = new Padding(0)
        };

        inner.Controls.Add(_lblTitle, 0, 0);
        inner.Controls.Add(_lblValue, 0, 1);
        inner.Controls.Add(_lblSub, 0, 2);

        Controls.Add(inner);
        Controls.Add(_stripe);
    }

    public void UpdateTile(string valueText, string subText, Color? valueColor = null)
    {
        if (_lblValue.Text != valueText) _lblValue.Text = valueText;
        if (_lblSub.Text != subText) _lblSub.Text = subText;
        if (valueColor.HasValue && _lblValue.ForeColor != valueColor.Value)
            _lblValue.ForeColor = valueColor.Value;
    }

    public void ApplyTheme(Color accentColor, DashboardThemePalette palette)
    {
        _borderColor = palette.CardBorder;
        BackColor = palette.TileBackground;
        _stripe.BackColor = accentColor;
        _lblTitle.ForeColor = palette.SecondaryText;
        _lblValue.ForeColor = palette.PrimaryText;
        _lblSub.ForeColor = palette.MutedText;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_borderColor, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
    }
}
