using System.Drawing;
using System.Runtime.InteropServices;
using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;

namespace CarbonMonitor.App.Overlay;

/// <summary>
/// Compact, text-first, always-on-top overlay toggled by global hotkey.
/// Click-through by default (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE) with optional interactive drag mode.
/// Avoids expensive animations/redraws and only updates labels when displayed text changes.
/// </summary>
public sealed class OverlayForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExTopMost = 0x00000008;
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly TableLayoutPanel _container;
    private readonly List<Label> _lineLabels = new();
    private readonly Label _hintLabel;
    private readonly System.Windows.Forms.Timer _autoHideTimer;
    private readonly AlertEvaluator _alertEvaluator = new();

    private OverlayConfig _config = new();
    private List<MetricConfig> _metricConfigs = new();
    private bool _showUnavailable;
    private bool _interactiveMode;
    private string _lastRenderedSignature = string.Empty;
    private Point _dragStartOffset;
    private bool _dragging;
    private Font? _cachedOverlayFont;
    private string _cachedFontFamily = string.Empty;
    private int _cachedFontSize;

    public event EventHandler<(int X, int Y)>? CustomPositionChanged;

    public OverlayForm()
    {
        SuspendLayout();

        Text = "Carbon Monitor Overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 20, 24);
        TransparencyKey = Color.Magenta;
        Opacity = 0.92;
        Padding = new Padding(8, 6, 8, 6);

        _container = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _hintLabel = new Label
        {
            Text = "DRAG TO MOVE (Interactive Mode)",
            AutoSize = true,
            ForeColor = Color.Gold,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Visible = false,
            Margin = new Padding(0, 0, 0, 4)
        };
        _container.Controls.Add(_hintLabel);

        Controls.Add(_container);

        _autoHideTimer = new System.Windows.Forms.Timer();
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            HideOverlay();
        };

        // Enable dragging when interactive mode is active
        MouseDown += OnOverlayMouseDown;
        MouseMove += OnOverlayMouseMove;
        MouseUp += OnOverlayMouseUp;
        _container.MouseDown += OnOverlayMouseDown;
        _container.MouseMove += OnOverlayMouseMove;
        _container.MouseUp += OnOverlayMouseUp;

        ResumeLayout(true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTopMost | WsExNoActivate;
            if (!_interactiveMode)
                cp.ExStyle |= WsExTransparent;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void ApplySettings(AppSettings settings)
    {
        _config = settings.Overlay;
        _metricConfigs = settings.Metrics.OrderBy(m => m.DisplayOrder).ToList();
        _showUnavailable = settings.ShowUnavailableMetrics;
        SetInteractiveMode(_config.InteractiveMode);

        Opacity = Math.Clamp(_config.TextOpacity, 0.15, 1.0);
        if (_config.ShowBackground)
        {
            TransparencyKey = Color.Empty;
            int bgGray = _config.HighContrast ? 0 : 18;
            BackColor = Color.FromArgb(bgGray, bgGray, _config.HighContrast ? 0 : 22);
            // Adjust form opacity when background panel is shown
            double bgBlend = Math.Clamp((_config.TextOpacity + _config.BackgroundOpacity) * 0.5, 0.25, 1.0);
            Opacity = _config.HighContrast ? 0.98 : bgBlend;
        }
        else
        {
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
        }

        Padding = _config.CompactSpacing ? new Padding(7, 4, 7, 4) : new Padding(12, 8, 12, 8);
        _lastRenderedSignature = string.Empty; // Force label restyle on next update

        if (_config.AutoHideSeconds is int sec && sec > 0 && Visible)
        {
            _autoHideTimer.Interval = sec * 1000;
            _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
        }

        UpdatePosition();
    }

    public void SetInteractiveMode(bool interactive)
    {
        _interactiveMode = interactive;
        _hintLabel.Visible = interactive;
        if (IsHandleCreated)
        {
            int exStyle = GetWindowLong(Handle, GwlExStyle);
            if (interactive)
                exStyle &= ~WsExTransparent;
            else
                exStyle |= WsExTransparent;
            SetWindowLong(Handle, GwlExStyle, exStyle);
        }
    }

    public void ShowOverlay()
    {
        if (!Visible)
        {
            UpdatePosition();
            Show();
            TopMost = true;
            if (_config.AutoHideSeconds is int sec && sec > 0)
            {
                _autoHideTimer.Interval = sec * 1000;
                _autoHideTimer.Start();
            }
        }
    }

    public void HideOverlay()
    {
        _autoHideTimer.Stop();
        if (Visible)
        {
            Hide();
        }
    }

    public void ToggleOverlay()
    {
        if (Visible) HideOverlay();
        else ShowOverlay();
    }

    public void UpdateReadings(IReadOnlyList<SensorReading> readings)
    {
        if (!Visible) return;

        var lookup = new Dictionary<string, SensorReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in readings)
        {
            lookup.TryAdd(r.MetricId, r);
        }

        var linesToDisplay = new List<(string Text, Color Color)>();
        var metricList = _metricConfigs.Count > 0
            ? _metricConfigs
            : AppSettings.CreateDefaultMetrics();

        foreach (var mc in metricList.Where(m => m.Enabled).OrderBy(m => m.DisplayOrder))
        {
            if (!lookup.TryGetValue(mc.MetricId, out var reading))
            {
                if (_showUnavailable)
                {
                    var label = mc.CustomLabel ?? mc.MetricId;
                    linesToDisplay.Add(($"{label}: N/A", Color.LightGray));
                }
                continue;
            }

            if (reading.Availability != SensorAvailability.Available)
            {
                if (_showUnavailable)
                {
                    var label = mc.CustomLabel ?? reading.DisplayName;
                    linesToDisplay.Add(($"{label}: N/A", Color.Silver));
                }
                continue;
            }

            var severity = _alertEvaluator.GetSeverity(reading, mc);
            string prefix = _config.WarningHighlight
                ? severity switch
                {
                    AlertSeverity.Critical => "[!!] ",
                    AlertSeverity.Warning => "[!] ",
                    _ => ""
                }
                : "";

            Color color = GetTextColor(severity);
            string displayLabel = mc.CustomLabel ?? GetShortLabel(reading);
            linesToDisplay.Add(($"{prefix}{displayLabel}: {reading.DisplayValue}", color));
        }

        if (linesToDisplay.Count == 0)
        {
            linesToDisplay.Add(("Carbon Monitor: Waiting for enabled sensors...", Color.LightGray));
        }

        // Skip UI layout updates if rendered text & colors are unchanged
        string sig = string.Join("|", linesToDisplay.Select(x => $"{x.Text}:{x.Color.ToArgb()}"));
        if (sig == _lastRenderedSignature)
            return;

        _lastRenderedSignature = sig;
        RenderLines(linesToDisplay);
        UpdatePosition();
    }

    private void RenderLines(List<(string Text, Color Color)> lines)
    {
        _container.SuspendLayout();

        var font = CreateOverlayFont();
        var margin = _config.CompactSpacing ? new Padding(0, 1, 0, 1) : new Padding(0, 3, 0, 3);

        while (_lineLabels.Count < lines.Count)
        {
            var lbl = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lbl.MouseDown += OnOverlayMouseDown;
            lbl.MouseMove += OnOverlayMouseMove;
            lbl.MouseUp += OnOverlayMouseUp;
            _lineLabels.Add(lbl);
            _container.Controls.Add(lbl);
        }

        for (int i = 0; i < _lineLabels.Count; i++)
        {
            var lbl = _lineLabels[i];
            if (i < lines.Count)
            {
                lbl.Text = lines[i].Text;
                lbl.ForeColor = lines[i].Color;
                lbl.Font = font;
                lbl.Margin = margin;
                lbl.Visible = true;
            }
            else
            {
                lbl.Visible = false;
            }
        }

        _container.ResumeLayout(true);
    }

    private Font CreateOverlayFont()
    {
        int size = Math.Clamp(_config.FontSize, 8, 36);
        string family = string.IsNullOrWhiteSpace(_config.FontFamily) ? "Segoe UI" : _config.FontFamily;

        if (_cachedOverlayFont is not null &&
            _cachedFontSize == size &&
            string.Equals(_cachedFontFamily, family, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedOverlayFont;
        }

        _cachedOverlayFont?.Dispose();
        try
        {
            _cachedOverlayFont = new Font(family, size, FontStyle.Bold);
        }
        catch
        {
            _cachedOverlayFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        }

        _cachedFontFamily = family;
        _cachedFontSize = size;
        return _cachedOverlayFont;
    }

    private Color GetTextColor(AlertSeverity severity)
    {
        if (!_config.WarningHighlight)
            return _config.HighContrast ? Color.White : Color.FromArgb(240, 244, 248);

        return severity switch
        {
            AlertSeverity.Critical => _config.HighContrast ? Color.FromArgb(255, 90, 90) : Color.Salmon,
            AlertSeverity.Warning => Color.Gold,
            _ => _config.HighContrast ? Color.FromArgb(144, 255, 144) : Color.FromArgb(235, 245, 240)
        };
    }

    public void UpdatePosition()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0) return;

        int idx = Math.Clamp(_config.DisplayIndex, 0, screens.Length - 1);
        var bounds = screens[idx].WorkingArea;
        int ox = _config.OffsetX;
        int oy = _config.OffsetY;

        Point target = _config.Position switch
        {
            "TopLeft" => new Point(bounds.Left + ox, bounds.Top + oy),
            "BottomRight" => new Point(bounds.Right - Width - ox, bounds.Bottom - Height - oy),
            "BottomLeft" => new Point(bounds.Left + ox, bounds.Bottom - Height - oy),
            "Custom" when _config.CustomX.HasValue && _config.CustomY.HasValue =>
                new Point(
                    Math.Clamp(_config.CustomX.Value, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)),
                    Math.Clamp(_config.CustomY.Value, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height))),
            _ => new Point(bounds.Right - Width - ox, bounds.Top + oy) // TopRight default
        };

        Location = target;
    }

    private static string GetShortLabel(SensorReading r) => r.MetricId switch
    {
        "cpu.usage.total" => "CPU",
        "lhm.cpu.temperature.package" => "CPU Temp",
        "cpu.temperature.thermalzone" => "CPU Zone",
        "cpu.clock.current" => "CPU Clock",
        "lhm.cpu.power" => "CPU Power",
        "gpu.usage" => "GPU",
        "gpu.temperature" => "GPU Temp",
        "gpu.clock" => "GPU Clock",
        "gpu.memory.dedicated" => "VRAM",
        "gpu.memory.shared" => "GPU Shared",
        "memory.usage.percent" => "RAM %",
        "memory.used" => "RAM",
        "disk.usage.percent" => "Disk",
        "disk.read.speed" => "Disk R",
        "disk.write.speed" => "Disk W",
        "storage.temperature" => "SSD Temp",
        "cooling.fan.rpm" => "Fan",
        "game.fps" => "FPS",
        "game.frametime" => "Frame",
        "game.fps.1percent_low" => "1% Low",
        "network.ping.latency" => "Ping",
        "network.ping.packetloss" => "Net Loss",
        "network.download.speed" => "Net Down",
        "network.upload.speed" => "Net Up",
        _ => r.DisplayName
    };

    private void OnOverlayMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_interactiveMode || e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartOffset = e.Location;
    }

    private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_interactiveMode || !_dragging) return;
        var screenPos = PointToScreen(e.Location);
        Location = new Point(screenPos.X - _dragStartOffset.X, screenPos.Y - _dragStartOffset.Y);
    }

    private void OnOverlayMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _config.Position = "Custom";
        _config.CustomX = Location.X;
        _config.CustomY = Location.Y;
        CustomPositionChanged?.Invoke(this, (Location.X, Location.Y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Dispose();
            _cachedOverlayFont?.Dispose();
            _cachedOverlayFont = null;
        }
        base.Dispose(disposing);
    }
}
