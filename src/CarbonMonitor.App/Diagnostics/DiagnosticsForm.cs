using System.Text;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using CarbonMonitor.Infrastructure.Diagnostics;
using CarbonMonitor.Infrastructure.Elevation;

namespace CarbonMonitor.App.Diagnostics;

/// <summary>
/// Hardware and sensor diagnostics window displaying all detected sensors,
/// provider health, permission requirements, last successful update timestamps,
/// failure reasons, deduplicated GPU summary, and local report export.
/// </summary>
public sealed class DiagnosticsForm : Form
{
    private readonly MonitoringEngine? _monitoringEngine;
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;

    private Panel _healthPanel = null!;
    private TextBox _txtFilter = null!;
    private CheckBox _chkShowOnlyIssues = null!;
    private DataGridView _dgvSensors = null!;
    private CheckBox _chkAutoRefresh = null!;
    private Button _btnRefresh = null!;
    private Button _btnExport = null!;
    private Button _btnElevate = null!;
    private Label _lblSensorCount = null!;
    private bool _shouldClose;

    public event EventHandler? ElevateRequested;

    public DiagnosticsForm(MonitoringEngine? monitoringEngine = null)
    {
        _monitoringEngine = monitoringEngine;
        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _autoRefreshTimer.Tick += (_, _) => { if (Visible && WindowState != FormWindowState.Minimized) RefreshSensors(); };
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Carbon Monitor - Hardware & Sensor Diagnostics";
        Size = new Size(1120, 680);
        MinimumSize = new Size(860, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = true;
        Icon = SystemIcons.Information;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        _healthPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MinimumSize = new Size(0, 60),
            Padding = new Padding(6),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 248, 250)
        };
        _healthPanel.Controls.Add(new Label
        {
            Text = "Provider Health & Physical GPU Deduplication Summary (Local Only)",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        });
        mainLayout.Controls.Add(_healthPanel, 0, 0);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 4)
        };
        filterBar.Controls.Add(new Label { Text = "Filter sensors:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        _txtFilter = new TextBox { Width = 240, PlaceholderText = "Search sensor, provider, or status..." };
        _txtFilter.TextChanged += (_, _) => RefreshSensors();
        filterBar.Controls.Add(_txtFilter);

        _chkShowOnlyIssues = new CheckBox { Text = "Show only unavailable / permission-restricted", AutoSize = true, Margin = new Padding(12, 4, 0, 0) };
        _chkShowOnlyIssues.CheckedChanged += (_, _) => RefreshSensors();
        filterBar.Controls.Add(_chkShowOnlyIssues);
        mainLayout.Controls.Add(filterBar, 0, 1);

        _dgvSensors = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        _dgvSensors.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "SensorName", HeaderText = "Sensor Name", FillWeight = 15 },
            new DataGridViewTextBoxColumn { Name = "MetricId", HeaderText = "Metric ID", FillWeight = 14 },
            new DataGridViewTextBoxColumn { Name = "Provider", HeaderText = "Provider", FillWeight = 12 },
            new DataGridViewTextBoxColumn { Name = "Hardware", HeaderText = "Hardware", FillWeight = 13 },
            new DataGridViewTextBoxColumn { Name = "CurrentReading", HeaderText = "Current Reading", FillWeight = 9 },
            new DataGridViewTextBoxColumn { Name = "SensorStatus", HeaderText = "Sensor Status", FillWeight = 10 },
            new DataGridViewTextBoxColumn { Name = "Permissions", HeaderText = "Permission Req.", FillWeight = 9 },
            new DataGridViewTextBoxColumn { Name = "LastSuccess", HeaderText = "Last Success", FillWeight = 9 },
            new DataGridViewTextBoxColumn { Name = "FailureReason", HeaderText = "Failure / Status Reason", FillWeight = 19 }
        );
        mainLayout.Controls.Add(_dgvSensors, 0, 2);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0)
        };

        _chkAutoRefresh = new CheckBox { Text = "Auto-refresh (3s)", Checked = true, AutoSize = true, Margin = new Padding(0, 4, 12, 0) };
        _chkAutoRefresh.CheckedChanged += (_, _) =>
        {
            if (_chkAutoRefresh.Checked && Visible) _autoRefreshTimer.Start();
            else _autoRefreshTimer.Stop();
        };
        toolbar.Controls.Add(_chkAutoRefresh);

        _btnRefresh = new Button { Text = "Refresh Now", Width = 100, Height = 28, Margin = new Padding(0, 0, 8, 0) };
        _btnRefresh.Click += (_, _) => RefreshSensors();
        toolbar.Controls.Add(_btnRefresh);

        _btnExport = new Button { Text = "Export Local Report...", Width = 155, Height = 28, Margin = new Padding(0, 0, 8, 0) };
        _btnExport.Click += BtnExport_Click;
        toolbar.Controls.Add(_btnExport);

        _btnElevate = new Button
        {
            Text = "Enable Elevated Sensors (UAC)",
            Width = 195,
            Height = 28,
            Margin = new Padding(0, 0, 12, 0),
            Visible = !ElevationHelper.IsProcessElevated()
        };
        _btnElevate.Click += (_, _) => ElevateRequested?.Invoke(this, EventArgs.Empty);
        toolbar.Controls.Add(_btnElevate);

        _lblSensorCount = new Label { Text = "Sensors: 0", AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
        toolbar.Controls.Add(_lblSensorCount);

        mainLayout.Controls.Add(toolbar, 0, 3);
        Controls.Add(mainLayout);
        ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshSensors();
        if (_chkAutoRefresh.Checked) _autoRefreshTimer.Start();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _chkAutoRefresh.Checked)
        {
            _autoRefreshTimer.Start();
            RefreshSensors();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_shouldClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _autoRefreshTimer.Stop();
        base.OnFormClosing(e);
    }

    public void ForceClose()
    {
        _shouldClose = true;
        Close();
    }

    private void RefreshSensors()
    {
        if (_monitoringEngine is null) return;
        UpdateSensorGrid(_monitoringEngine.LatestReadings);
        UpdateHealthSummary(_monitoringEngine.GetProviderHealthInfo(), _monitoringEngine.GetAggregatedGpus());
    }

    public void UpdateSensorGrid(IReadOnlyList<SensorReading> readings)
    {
        string filter = _txtFilter.Text.Trim();
        bool onlyIssues = _chkShowOnlyIssues.Checked;

        var filtered = readings.Where(r =>
        {
            if (onlyIssues && r.Availability == SensorAvailability.Available)
                return false;
            if (string.IsNullOrEmpty(filter))
                return true;

            return r.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   r.MetricId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   r.ProviderName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   r.Availability.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   (r.UnavailableReason?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).OrderBy(r => r.Category).ThenBy(r => r.ProviderName).ThenBy(r => r.MetricId).ToList();

        _dgvSensors.SuspendLayout();
        _dgvSensors.Rows.Clear();

        foreach (var r in filtered)
        {
            string perm = r.PermissionRequirement ?? (r.Availability == SensorAvailability.PermissionRequired ? "Administrator" : "None");
            string lastOk = r.LastSuccessfulUpdate.HasValue
                ? r.LastSuccessfulUpdate.Value.LocalDateTime.ToString("HH:mm:ss")
                : (r.Availability == SensorAvailability.Available ? r.Timestamp.LocalDateTime.ToString("HH:mm:ss") : "Never");
            string reason = string.Join(" | ", new[] { r.UnavailableReason, r.QualityNote }.Where(s => !string.IsNullOrWhiteSpace(s)));

            int idx = _dgvSensors.Rows.Add(
                r.DisplayName,
                r.MetricId,
                r.ProviderName,
                r.HardwareName,
                r.DisplayValue,
                r.Availability.ToString(),
                perm,
                lastOk,
                reason);

            _dgvSensors.Rows[idx].DefaultCellStyle.BackColor = r.Availability switch
            {
                SensorAvailability.Available => Color.FromArgb(232, 250, 234),
                SensorAvailability.PermissionRequired => Color.FromArgb(255, 248, 214),
                SensorAvailability.TemporarilyUnavailable => Color.FromArgb(255, 250, 226),
                SensorAvailability.Unsupported or SensorAvailability.NotDetected => Color.FromArgb(242, 242, 244),
                SensorAvailability.Error => Color.FromArgb(255, 224, 224),
                _ => SystemColors.Window
            };
        }

        _lblSensorCount.Text = $"Showing {filtered.Count} / {readings.Count} sensors ({readings.Count(r => r.Availability == SensorAvailability.Available)} available)";
        _dgvSensors.ResumeLayout();
    }

    public void UpdateHealthSummary(IReadOnlyList<ProviderHealthInfo> healthData, IReadOnlyList<PhysicalGpuSnapshot> gpus)
    {
        var toRemove = _healthPanel.Controls.Cast<Control>()
            .Where(c => Equals(c.Tag, "health"))
            .ToList();
        foreach (var c in toRemove) { _healthPanel.Controls.Remove(c); c.Dispose(); }

        int y = 26;
        if (gpus.Count > 0)
        {
            var gpuLine = string.Join("  |  ", gpus.Select(g =>
                $"{g.Name} [{(g.IsIntegrated ? "Integrated" : "Dedicated")}, key={g.CanonicalKey}, providers={g.ContributingProviders.Count}]"));
            var lblGpu = new Label
            {
                Text = $"Deduplicated Physical GPUs ({gpus.Count}): {gpuLine}",
                AutoSize = true,
                Location = new Point(8, y),
                Tag = "health",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.DarkSlateBlue
            };
            _healthPanel.Controls.Add(lblGpu);
            y += 20;
        }

        foreach (var h in healthData)
        {
            string statusTag = h.IsHealthy ? "[OK]" : $"[{h.Status}]";
            var lbl = new Label
            {
                Text = $"{statusTag} {h.ProviderName}: {h.SensorCount} active sensors" +
                       (string.IsNullOrWhiteSpace(h.ErrorMessage) ? "" : $" — {h.ErrorMessage}"),
                AutoSize = true,
                Location = new Point(8, y),
                Tag = "health",
                ForeColor = h.IsHealthy ? Color.DarkGreen : (h.Status == Core.Abstractions.ProviderHealth.Faulted ? Color.Firebrick : Color.DarkGoldenrod)
            };
            _healthPanel.Controls.Add(lbl);
            y += 18;
        }
        _healthPanel.MinimumSize = new Size(0, y + 6);
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_monitoringEngine is null) return;

        using var dlg = new SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"CarbonMonitor-Diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt",
            DefaultExt = "txt",
            Title = "Export Local Diagnostic Report"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var report = DiagnosticReportBuilder.BuildReport(
                _monitoringEngine.GetProviderHealthInfo(),
                _monitoringEngine.GetAllDetectedHardware(),
                _monitoringEngine.LatestReadings);

            File.WriteAllText(dlg.FileName, report, Encoding.UTF8);

            MessageBox.Show(
                "Local diagnostic report exported successfully.\n\n" +
                "PRIVACY REMINDER: Please review the file for usernames, machine names, " +
                "or hardware identifiers before sharing it with anyone.",
                "Export Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
