namespace CarbonMonitor.App.Tray;

/// <summary>
/// Manages the Windows notification area icon, context menu, status indicators, and alert balloons.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _miDashboard;
    private readonly ToolStripMenuItem _miToggleOverlay;
    private readonly ToolStripMenuItem _miSettings;
    private readonly ToolStripMenuItem _miDiagnostics;
    private readonly ToolStripMenuItem _miPauseResume;
    private readonly ToolStripMenuItem _miStatusHeader;
    private readonly ToolStripMenuItem _miExit;
    private Icon? _ownedIcon;
    private bool _disposed;

    public event EventHandler? DashboardRequested;
    public event EventHandler? OverlayToggleRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? PauseToggleRequested;
    public event EventHandler? ExitRequested;

    public TrayManager()
    {
        _menu = new ContextMenuStrip();

        _miStatusHeader = new ToolStripMenuItem("Status: Initializing...") { Enabled = false };
        _miDashboard = new ToolStripMenuItem("Open Dashboard");
        _miDashboard.Font = new Font(_miDashboard.Font, FontStyle.Bold);
        _miDashboard.Click += (_, _) => DashboardRequested?.Invoke(this, EventArgs.Empty);

        _miToggleOverlay = new ToolStripMenuItem("Show Overlay (Alt + R)");
        _miToggleOverlay.Click += (_, _) => OverlayToggleRequested?.Invoke(this, EventArgs.Empty);

        _miSettings = new ToolStripMenuItem("Settings...");
        _miSettings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _miDiagnostics = new ToolStripMenuItem("Hardware Diagnostics...");
        _miDiagnostics.Click += (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

        _miPauseResume = new ToolStripMenuItem("Pause Monitoring");
        _miPauseResume.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

        _miExit = new ToolStripMenuItem("Exit");
        _miExit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miStatusHeader,
            new ToolStripSeparator(),
            _miDashboard,
            _miToggleOverlay,
            new ToolStripSeparator(),
            _miSettings,
            _miDiagnostics,
            new ToolStripSeparator(),
            _miPauseResume,
            new ToolStripSeparator(),
            _miExit
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Carbon Monitor",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => DashboardRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateState(bool isMonitoringActive, bool isPaused, bool isOverlayVisible, string hotkeyText, string modeLabel)
    {
        string monText = isPaused ? "Paused" : (isMonitoringActive ? "Active" : $"Standby ({modeLabel})");
        string ovlText = isOverlayVisible ? "Visible" : "Hidden";

        _miStatusHeader.Text = $"Monitoring: {monText} | Overlay: {ovlText}";
        _miToggleOverlay.Text = isOverlayVisible
            ? $"Hide Overlay ({hotkeyText})"
            : $"Show Overlay ({hotkeyText})";
        _miPauseResume.Text = isPaused ? "Resume Monitoring" : "Pause Monitoring";

        // NotifyIcon.Text limit is 63 characters on older Windows builds; keep under 63 chars
        string tooltip = $"Carbon Monitor [{monText} | Ovl: {ovlText}]";
        if (tooltip.Length > 63)
            tooltip = tooltip[..63];
        _notifyIcon.Text = tooltip;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 4000)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(timeoutMs, title, message, icon);
    }

    private Icon LoadAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "carbon-monitor.ico");
            if (File.Exists(iconPath))
            {
                _ownedIcon = new Icon(iconPath);
                return _ownedIcon;
            }
        }
        catch
        {
            // Fall back to system icon
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _ownedIcon?.Dispose();
    }
}
