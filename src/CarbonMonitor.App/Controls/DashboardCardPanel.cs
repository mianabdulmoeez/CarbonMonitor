using System.Drawing;

namespace CarbonMonitor.App.Controls;

/// <summary>
/// Modern dark card panel with a colored top accent stripe, header title, status pill, and body container.
/// </summary>
public sealed class DashboardCardPanel : Panel
{
    private readonly Panel _accentBar;
    private readonly Panel _headerPanel;
    private readonly Label _lblTitle;
    private readonly Label _lblBadge;
    private readonly Panel _bodyPanel;
    private Color _borderColor = Color.FromArgb(40, 49, 66);

    public Panel Body => _bodyPanel;

    public DashboardCardPanel(string title)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(6);
        Padding = new Padding(1);
        BackColor = Color.FromArgb(22, 27, 37);

        _accentBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = Color.FromArgb(56, 189, 248)
        };

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(12, 6, 12, 2)
        };

        _lblTitle = new Label
        {
            Text = title.ToUpperInvariant(),
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(236, 241, 248),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblBadge = new Label
        {
            Text = "STANDBY",
            Dock = DockStyle.Right,
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 167, 184),
            Padding = new Padding(6, 2, 6, 2),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _headerPanel.Controls.Add(_lblBadge);
        _headerPanel.Controls.Add(_lblTitle);

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 4, 12, 10)
        };

        Controls.Add(_bodyPanel);
        Controls.Add(_headerPanel);
        Controls.Add(_accentBar);
    }

    public void SetTitle(string title)
    {
        var upper = title.ToUpperInvariant();
        if (_lblTitle.Text != upper)
            _lblTitle.Text = upper;
    }

    public void SetBadge(string text, Color foreColor, Color backColor)
    {
        if (_lblBadge.Text != text) _lblBadge.Text = text;
        if (_lblBadge.ForeColor != foreColor) _lblBadge.ForeColor = foreColor;
        if (_lblBadge.BackColor != backColor) _lblBadge.BackColor = backColor;
    }

    public void ApplyTheme(Color accentColor, DashboardThemePalette palette)
    {
        _borderColor = palette.CardBorder;
        BackColor = palette.CardBackground;
        _accentBar.BackColor = accentColor;
        _headerPanel.BackColor = palette.CardBackground;
        _bodyPanel.BackColor = palette.CardBackground;
        _lblTitle.ForeColor = palette.PrimaryText;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_borderColor, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
    }
}
