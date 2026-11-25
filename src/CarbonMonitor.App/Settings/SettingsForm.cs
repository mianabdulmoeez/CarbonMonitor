using CarbonMonitor.App.Hotkey;
using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Infrastructure.Elevation;
using CarbonMonitor.Infrastructure.SettingsStorage;
using CarbonMonitor.Infrastructure.Startup;

namespace CarbonMonitor.App.Settings;

/// <summary>
/// Tabbed settings GUI covering General & Dashboard Appearance, Hotkey, Overlay,
/// Metrics, Gaming/Network, and Sensor Permission guidance.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly LocalSettingsService _settingsService;
    private readonly HotkeyManager? _hotkeyManager;
    private AppSettings _settings = new();
    private readonly List<MetricRow> _metricRows = new();

    private TabControl _tabControl = null!;

    // General & Dashboard
    private CheckBox _chkLaunchAtStartup = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkStartOverlayVisible = null!;
    private ComboBox _cboMonitoringMode = null!;
    private ComboBox _cboDashboardTheme = null!;
    private CheckBox _chkShowDashboardGraphs = null!;
    private CheckBox _chkShowPerCoreCpu = null!;
    private CheckBox _chkReduceOnBattery = null!;
    private CheckBox _chkPauseOnBattery = null!;
    private CheckBox _chkEnableAlerts = null!;
    private NumericUpDown _nudPollingMs = null!;

    // Hotkey
    private HotkeyRecorder _hotkeyRecorder = null!;
    private Button _btnRecordHotkey = null!;
    private Button _btnResetHotkey = null!;
    private Button _btnTestHotkey = null!;
    private Label _lblHotkeyStatus = null!;

    // Overlay
    private ComboBox _cboPosition = null!;
    private ComboBox _cboDisplay = null!;
    private NumericUpDown _nudFontSize = null!;
    private ComboBox _cboFontFamily = null!;
    private TrackBar _trkTextOpacity = null!;
    private Label _lblTextOpacityValue = null!;
    private TrackBar _trkBgOpacity = null!;
    private Label _lblBgOpacityValue = null!;
    private CheckBox _chkShowBackground = null!;
    private CheckBox _chkCompactSpacing = null!;
    private CheckBox _chkWarningHighlight = null!;
    private CheckBox _chkHighContrast = null!;
    private CheckBox _chkInteractiveMode = null!;
    private NumericUpDown _nudAutoHide = null!;
    private Button _btnPreviewOverlay = null!;

    // Metrics
    private DataGridView _dgvMetrics = null!;
    private Button _btnMoveUp = null!;
    private Button _btnMoveDown = null!;
    private CheckBox _chkShowUnavailable = null!;

    // Game & Network
    private ListBox _lstGameProcesses = null!;
    private TextBox _txtAddProcess = null!;
    private Button _btnAddProcess = null!;
    private Button _btnRemoveProcess = null!;
    private Button _btnValorantPreset = null!;
    private CheckBox _chkEnableFps = null!;
    private TextBox _txtPingTarget = null!;
    private NumericUpDown _nudPingInterval = null!;

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? PreviewOverlayRequested;
    public event EventHandler? ElevateRequested;

    public SettingsForm(LocalSettingsService settingsService, HotkeyManager? hotkeyManager = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _hotkeyManager = hotkeyManager;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Carbon Monitor - Settings";
        Size = new Size(760, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(CreateGeneralAndDashboardTab());
        _tabControl.TabPages.Add(CreateHotkeyTab());
        _tabControl.TabPages.Add(CreateOverlayTab());
        _tabControl.TabPages.Add(CreateMetricsTab());
        _tabControl.TabPages.Add(CreateGameTab());
        _tabControl.TabPages.Add(CreatePermissionsTab());
        mainLayout.Controls.Add(_tabControl, 0, 0);
        mainLayout.Controls.Add(CreateBottomPanel(), 0, 1);

        Controls.Add(mainLayout);
        ResumeLayout(true);
    }

    private TabPage CreateGeneralAndDashboardTab()
    {
        var page = new TabPage("General & Dashboard");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            Padding = new Padding(12)
        };
        for (int i = 0; i < 11; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _chkLaunchAtStartup = new CheckBox { Text = "Launch at Windows sign-in (opt-in, per-user HKCU Run key)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkStartMinimized = new CheckBox { Text = "Start minimized to system tray", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkStartOverlayVisible = new CheckBox { Text = "Show overlay automatically on startup", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };

        var modePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 4) };
        modePanel.Controls.Add(new Label { Text = "Monitoring mode:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _cboMonitoringMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        _cboMonitoringMode.Items.AddRange(new object[] { "Always Monitor", "Selected Game Only", "Overlay Visible Only" });
        modePanel.Controls.Add(_cboMonitoringMode);

        var pollPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 4) };
        pollPanel.Controls.Add(new Label { Text = "Refresh interval (ms):", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _nudPollingMs = new NumericUpDown { Minimum = 500, Maximum = 15000, Increment = 250, Value = 2000, Width = 80 };
        pollPanel.Controls.Add(_nudPollingMs);

        var themePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 4) };
        themePanel.Controls.Add(new Label { Text = "Dashboard theme:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _cboDashboardTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _cboDashboardTheme.Items.AddRange(new object[] { "DarkGaming", "HighContrast", "Light" });
        themePanel.Controls.Add(_cboDashboardTheme);

        _chkShowDashboardGraphs = new CheckBox { Text = "Show historical sparkline graphs on Dashboard", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkShowPerCoreCpu = new CheckBox { Text = "Show per-thread CPU utilization bars", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkReduceOnBattery = new CheckBox { Text = "Reduce polling frequency when on laptop battery (5000 ms)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkPauseOnBattery = new CheckBox { Text = "Pause background polling when on battery (unless Dashboard is open)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        _chkEnableAlerts = new CheckBox { Text = "Show rate-limited warning/critical temperature & load alerts", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };

        layout.Controls.Add(_chkLaunchAtStartup, 0, 0);
        layout.Controls.Add(_chkStartMinimized, 0, 1);
        layout.Controls.Add(_chkStartOverlayVisible, 0, 2);
        layout.Controls.Add(modePanel, 0, 3);
        layout.Controls.Add(pollPanel, 0, 4);
        layout.Controls.Add(themePanel, 0, 5);
        layout.Controls.Add(_chkShowDashboardGraphs, 0, 6);
        layout.Controls.Add(_chkShowPerCoreCpu, 0, 7);
        layout.Controls.Add(_chkReduceOnBattery, 0, 8);
        layout.Controls.Add(_chkPauseOnBattery, 0, 9);
        layout.Controls.Add(_chkEnableAlerts, 0, 10);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateHotkeyTab()
    {
        var page = new TabPage("Hotkey");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12)
        };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(new Label
        {
            Text = "Global Overlay Toggle Shortcut (Win32 RegisterHotKey):",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 6)
        }, 0, 0);

        _hotkeyRecorder = new HotkeyRecorder { Width = 280, Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(_hotkeyRecorder, 0, 1);

        var btnPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 10) };
        _btnRecordHotkey = new Button { Text = "Record New Hotkey", Width = 140, Height = 30 };
        _btnRecordHotkey.Click += (_, _) =>
        {
            _hotkeyRecorder.StartRecording();
            _lblHotkeyStatus.Text = "Press a key combination including Alt, Ctrl, or Shift...";
            _lblHotkeyStatus.ForeColor = SystemColors.ControlText;
        };

        _btnTestHotkey = new Button { Text = "Test Registration", Width = 130, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        _btnTestHotkey.Click += (_, _) => TestHotkeyRegistration();

        _btnResetHotkey = new Button { Text = "Reset Default (Alt+R)", Width = 150, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        _btnResetHotkey.Click += (_, _) =>
        {
            _hotkeyRecorder.SetHotkey(0x0001, (uint)Keys.R);
            TestHotkeyRegistration();
        };

        btnPanel.Controls.AddRange(new Control[] { _btnRecordHotkey, _btnTestHotkey, _btnResetHotkey });
        layout.Controls.Add(btnPanel, 0, 2);

        _lblHotkeyStatus = new Label { AutoSize = true, Text = "", Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(_lblHotkeyStatus, 0, 3);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Carbon Monitor uses Windows RegisterHotKey instead of a global low-level keyboard hook.\n" +
                   "If another app already owns Alt+R, registration reports a conflict so you can pick a\n" +
                   "conflict-free shortcut such as Ctrl+Shift+F10."
        }, 0, 4);

        _hotkeyRecorder.HotkeyRecorded += (_, e) =>
        {
            _lblHotkeyStatus.Text = $"Recorded: {HotkeyRecorder.FormatHotkey(e.Modifiers, e.Key)} (click Test or Save).";
            _lblHotkeyStatus.ForeColor = Color.DarkGreen;
        };

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateOverlayTab()
    {
        var page = new TabPage("Overlay");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 13,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        int row = 0;
        layout.Controls.Add(CreateLabel("Position:"), 0, row);
        _cboPosition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _cboPosition.Items.AddRange(new object[] { "TopRight", "TopLeft", "BottomRight", "BottomLeft", "Custom" });
        layout.Controls.Add(_cboPosition, 1, row++);

        layout.Controls.Add(CreateLabel("Display:"), 0, row);
        _cboDisplay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        PopulateDisplays();
        layout.Controls.Add(_cboDisplay, 1, row++);

        layout.Controls.Add(CreateLabel("Font size:"), 0, row);
        _nudFontSize = new NumericUpDown { Minimum = 8, Maximum = 32, Value = 12, Width = 70 };
        layout.Controls.Add(_nudFontSize, 1, row++);

        layout.Controls.Add(CreateLabel("Font family:"), 0, row);
        _cboFontFamily = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _cboFontFamily.Items.AddRange(new object[] { "Segoe UI", "Consolas", "Cascadia Mono", "Arial", "Courier New" });
        layout.Controls.Add(_cboFontFamily, 1, row++);

        layout.Controls.Add(CreateLabel("Text opacity:"), 0, row);
        var textOpPanel = new FlowLayoutPanel { AutoSize = true };
        _trkTextOpacity = new TrackBar { Minimum = 15, Maximum = 100, Value = 95, TickFrequency = 10, Width = 190 };
        _lblTextOpacityValue = new Label { Text = "95%", AutoSize = true, Margin = new Padding(4, 8, 0, 0) };
        _trkTextOpacity.ValueChanged += (_, _) => _lblTextOpacityValue.Text = $"{_trkTextOpacity.Value}%";
        textOpPanel.Controls.AddRange(new Control[] { _trkTextOpacity, _lblTextOpacityValue });
        layout.Controls.Add(textOpPanel, 1, row++);

        layout.Controls.Add(CreateLabel("Background opacity:"), 0, row);
        var bgOpPanel = new FlowLayoutPanel { AutoSize = true };
        _trkBgOpacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 70, TickFrequency = 10, Width = 190 };
        _lblBgOpacityValue = new Label { Text = "70%", AutoSize = true, Margin = new Padding(4, 8, 0, 0) };
        _trkBgOpacity.ValueChanged += (_, _) => _lblBgOpacityValue.Text = $"{_trkBgOpacity.Value}%";
        bgOpPanel.Controls.AddRange(new Control[] { _trkBgOpacity, _lblBgOpacityValue });
        layout.Controls.Add(bgOpPanel, 1, row++);

        _chkShowBackground = new CheckBox { Text = "Show semi-transparent background panel", AutoSize = true };
        layout.Controls.Add(_chkShowBackground, 1, row++);

        _chkCompactSpacing = new CheckBox { Text = "Compact spacing", AutoSize = true };
        layout.Controls.Add(_chkCompactSpacing, 1, row++);

        _chkWarningHighlight = new CheckBox { Text = "Warning/critical highlights ([!] / [!!] + color)", AutoSize = true };
        layout.Controls.Add(_chkWarningHighlight, 1, row++);

        _chkHighContrast = new CheckBox { Text = "High contrast overlay mode", AutoSize = true };
        layout.Controls.Add(_chkHighContrast, 1, row++);

        _chkInteractiveMode = new CheckBox { Text = "Interactive drag mode (uncheck for click-through in games)", AutoSize = true };
        layout.Controls.Add(_chkInteractiveMode, 1, row++);

        var bottomPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        bottomPanel.Controls.Add(new Label { Text = "Auto-hide (sec, 0=off):", AutoSize = true, Margin = new Padding(0, 5, 4, 0) });
        _nudAutoHide = new NumericUpDown { Minimum = 0, Maximum = 300, Value = 0, Width = 65 };
        bottomPanel.Controls.Add(_nudAutoHide);
        _btnPreviewOverlay = new Button { Text = "Preview Overlay", Width = 120, Height = 28, Margin = new Padding(16, 0, 0, 0) };
        _btnPreviewOverlay.Click += (_, _) => PreviewOverlayRequested?.Invoke(this, BuildSettingsFromUi());
        bottomPanel.Controls.Add(_btnPreviewOverlay);
        layout.Controls.Add(bottomPanel, 0, row++);
        layout.SetColumnSpan(bottomPanel, 2);

        var fsNote = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Tip: If an exclusive fullscreen game hides external Windows overlays, use Borderless Windowed mode. Carbon Monitor never injects DLLs."
        };
        layout.Controls.Add(fsNote, 0, row);
        layout.SetColumnSpan(fsNote, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateMetricsTab()
    {
        var page = new TabPage("Overlay Metrics");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _dgvMetrics = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        _dgvMetrics.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Show", FillWeight = 10 });
        _dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn { Name = "MetricName", HeaderText = "Metric", ReadOnly = true, FillWeight = 32 });
        _dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Group", ReadOnly = true, FillWeight = 16 });
        _dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn { Name = "Warning", HeaderText = "Warn >=", FillWeight = 16 });
        _dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn { Name = "Critical", HeaderText = "Crit >=", FillWeight = 16 });
        _dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn { Name = "MetricId", HeaderText = "ID", Visible = false });

        _dgvMetrics.CellValueChanged += DgvMetrics_CellValueChanged;
        _dgvMetrics.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_dgvMetrics.IsCurrentCellDirty)
                _dgvMetrics.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        layout.Controls.Add(_dgvMetrics, 0, 0);

        var sidePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        _btnMoveUp = new Button { Text = "Move Up", Width = 100, Height = 30, Margin = new Padding(0, 0, 0, 6) };
        _btnMoveDown = new Button { Text = "Move Down", Width = 100, Height = 30 };
        _btnMoveUp.Click += (_, _) => MoveSelectedMetric(-1);
        _btnMoveDown.Click += (_, _) => MoveSelectedMetric(1);
        sidePanel.Controls.AddRange(new Control[] { _btnMoveUp, _btnMoveDown });
        layout.Controls.Add(sidePanel, 1, 0);

        _chkShowUnavailable = new CheckBox { Text = "Show unsupported/unavailable metrics as N/A in overlay", AutoSize = true };
        layout.Controls.Add(_chkShowUnavailable, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateGameTab()
    {
        var page = new TabPage("Game & Network");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var hdr = new Label { Text = "Watched game process names (for Selected Game Only mode & FPS telemetry):", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        layout.Controls.Add(hdr, 0, 0);
        layout.SetColumnSpan(hdr, 2);

        _lstGameProcesses = new ListBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_lstGameProcesses, 0, 1);

        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 0, 0, 0) };
        _txtAddProcess = new TextBox { Width = 200, PlaceholderText = "Process name (without .exe)" };
        _btnAddProcess = new Button { Text = "Add Process", Width = 120, Height = 28, Margin = new Padding(0, 6, 0, 4) };
        _btnAddProcess.Click += (_, _) =>
        {
            var name = _txtAddProcess.Text.Trim();
            if (!string.IsNullOrEmpty(name) && !_lstGameProcesses.Items.Contains(name))
                _lstGameProcesses.Items.Add(name);
            _txtAddProcess.Clear();
        };
        _btnRemoveProcess = new Button { Text = "Remove Selected", Width = 120, Height = 28, Margin = new Padding(0, 0, 0, 10) };
        _btnRemoveProcess.Click += (_, _) =>
        {
            if (_lstGameProcesses.SelectedIndex >= 0)
                _lstGameProcesses.Items.RemoveAt(_lstGameProcesses.SelectedIndex);
        };
        _btnValorantPreset = new Button { Text = "Add Valorant Preset", Width = 140, Height = 28 };
        _btnValorantPreset.Click += (_, _) =>
        {
            const string val = "VALORANT-Win64-Shipping";
            if (!_lstGameProcesses.Items.Contains(val))
                _lstGameProcesses.Items.Add(val);
        };
        actionPanel.Controls.AddRange(new Control[] { _txtAddProcess, _btnAddProcess, _btnRemoveProcess, _btnValorantPreset });
        layout.Controls.Add(actionPanel, 1, 1);

        _chkEnableFps = new CheckBox
        {
            Text = "Enable optional FPS / frame-time capture (external ETW / PresentMon; Vanguard compatibility not guaranteed)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4)
        };
        layout.Controls.Add(_chkEnableFps, 0, 2);
        layout.SetColumnSpan(_chkEnableFps, 2);

        var pingPanel = new FlowLayoutPanel { AutoSize = true };
        pingPanel.Controls.Add(new Label { Text = "ICMP Ping target:", AutoSize = true, Margin = new Padding(0, 4, 4, 0) });
        _txtPingTarget = new TextBox { Width = 140, Text = "8.8.8.8" };
        pingPanel.Controls.Add(_txtPingTarget);
        pingPanel.Controls.Add(new Label { Text = "Interval (sec):", AutoSize = true, Margin = new Padding(14, 4, 4, 0) });
        _nudPingInterval = new NumericUpDown { Minimum = 2, Maximum = 60, Value = 5, Width = 60 };
        pingPanel.Controls.Add(_nudPingInterval);
        layout.Controls.Add(pingPanel, 0, 3);
        layout.SetColumnSpan(pingPanel, 2);

        var netNote = new Label
        {
            Text = "Note: ICMP probe loss reflects general network reachability, NOT authoritative Valorant server packet loss.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        layout.Controls.Add(netNote, 0, 4);
        layout.SetColumnSpan(netNote, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreatePermissionsTab()
    {
        var page = new TabPage("Sensor Permissions");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        bool elevated = ElevationHelper.IsProcessElevated();

        layout.Controls.Add(new Label
        {
            Text = elevated
                ? "Current Status: Running with Administrator Privileges (Full Hardware Sensor Access Enabled)"
                : "Current Status: Standard User Mode (Least-Privilege Default)",
            AutoSize = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = elevated ? Color.DarkGreen : Color.DarkGoldenrod,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Sensors Available Without Administrator Rights:\n" +
                   "  • CPU overall & per-thread utilization, live turbo clock frequency, and ACPI Thermal Zone (\\_TZ.THM0)\n" +
                   "  • Intel UHD 620 / Windows WDDM GPU Engine utilization & shared memory\n" +
                   "  • Physical RAM usage, Drive capacity/volumes/read-write throughput, Drive health status, Network & ICMP ping\n\n" +
                   "Sensors Requiring Optional Administrator Elevation on Windows 11:\n" +
                   "  • Direct Intel/AMD CPU Package MSR temperature & CPU Package Power (LibreHardwareMonitor)\n" +
                   "  • NVMe / SATA SMART SSD temperature (MSFT_StorageReliabilityCounter)\n" +
                   "  • In-process DXGI ETW FPS session (unless using Performance Log Users or external PresentMon.exe)\n\n" +
                   "Hardware Limitation Note (Intel UHD 620):\n" +
                   "  • Intel UHD Graphics 620 does not expose a separate discrete GPU temperature sensor even when elevated.\n" +
                   "    Carbon Monitor never fabricates GPU temperature or substitutes CPU temperature.",
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);

        var btnElevate = new Button
        {
            Text = elevated ? "Already Running Elevated" : "Restart with Elevated Sensor Access (UAC)...",
            Enabled = !elevated,
            Width = 310,
            Height = 32
        };
        btnElevate.Click += (_, _) => ElevateRequested?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(btnElevate, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private FlowLayoutPanel CreateBottomPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 4, 0) };
        var btnCancel = new Button { Text = "Cancel", Width = 85, Height = 30 };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var btnSave = new Button { Text = "Save", Width = 85, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        btnSave.Click += BtnSave_Click;

        var btnImport = new Button { Text = "Import...", Width = 95, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        btnImport.Click += BtnImport_Click;

        var btnExport = new Button { Text = "Export...", Width = 95, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        btnExport.Click += BtnExport_Click;

        var btnReset = new Button { Text = "Reset Defaults", Width = 115, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        btnReset.Click += BtnResetDefaults_Click;

        panel.Controls.AddRange(new Control[] { btnCancel, btnSave, btnImport, btnExport, btnReset });
        return panel;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _settings = await _settingsService.LoadAsync();
        PopulateFromSettings(_settings);
    }

    private void PopulateFromSettings(AppSettings s)
    {
        _chkLaunchAtStartup.Checked = s.LaunchAtStartup;
        _chkStartMinimized.Checked = s.StartMinimized;
        _chkStartOverlayVisible.Checked = s.StartOverlayVisible;
        _cboMonitoringMode.SelectedIndex = Math.Clamp((int)s.MonitoringMode, 0, 2);
        _nudPollingMs.Value = Math.Clamp(s.PollingIntervalMs, 500, 15000);
        SelectComboItem(_cboDashboardTheme, s.DashboardTheme);
        _chkShowDashboardGraphs.Checked = s.ShowDashboardGraphs;
        _chkShowPerCoreCpu.Checked = s.ShowPerCoreCpu;
        _chkReduceOnBattery.Checked = s.ReducePollingOnBattery;
        _chkPauseOnBattery.Checked = s.PauseOnBattery;
        _chkEnableAlerts.Checked = s.EnableWarningAlerts;

        _hotkeyRecorder.SetHotkey((uint)s.Hotkey.Modifiers, (uint)s.Hotkey.Key);
        if (_hotkeyManager is not null && !_hotkeyManager.IsRegistered && !string.IsNullOrEmpty(_hotkeyManager.ConflictMessage))
        {
            _lblHotkeyStatus.Text = _hotkeyManager.ConflictMessage;
            _lblHotkeyStatus.ForeColor = Color.Firebrick;
        }
        else
        {
            _lblHotkeyStatus.Text = $"Active hotkey: {s.Hotkey.DisplayString}";
            _lblHotkeyStatus.ForeColor = Color.DarkGreen;
        }

        SelectComboItem(_cboPosition, s.Overlay.Position);
        SelectDisplayIndex(s.Overlay.DisplayIndex);
        _nudFontSize.Value = Math.Clamp(s.Overlay.FontSize, 8, 32);
        SelectComboItem(_cboFontFamily, s.Overlay.FontFamily);
        _trkTextOpacity.Value = Math.Clamp((int)Math.Round(s.Overlay.TextOpacity * 100), 15, 100);
        _lblTextOpacityValue.Text = $"{_trkTextOpacity.Value}%";
        _trkBgOpacity.Value = Math.Clamp((int)Math.Round(s.Overlay.BackgroundOpacity * 100), 0, 100);
        _lblBgOpacityValue.Text = $"{_trkBgOpacity.Value}%";
        _chkShowBackground.Checked = s.Overlay.ShowBackground;
        _chkCompactSpacing.Checked = s.Overlay.CompactSpacing;
        _chkWarningHighlight.Checked = s.Overlay.WarningHighlight;
        _chkHighContrast.Checked = s.Overlay.HighContrast;
        _chkInteractiveMode.Checked = s.Overlay.InteractiveMode;
        _nudAutoHide.Value = Math.Clamp(s.Overlay.AutoHideSeconds ?? 0, 0, 300);

        PopulateMetrics(s);
        _chkShowUnavailable.Checked = s.ShowUnavailableMetrics;

        _lstGameProcesses.Items.Clear();
        foreach (var gp in s.GameProcesses)
        {
            if (!string.IsNullOrWhiteSpace(gp.ProcessName))
                _lstGameProcesses.Items.Add(gp.ProcessName);
        }
        _chkEnableFps.Checked = s.EnableFpsCapture;
        _txtPingTarget.Text = s.PingTarget;
        _nudPingInterval.Value = Math.Clamp(s.PingIntervalSeconds, 2, 60);
    }

    private void PopulateMetrics(AppSettings s)
    {
        _metricRows.Clear();
        _dgvMetrics.Rows.Clear();

        var existing = s.Metrics.ToDictionary(m => m.MetricId, StringComparer.OrdinalIgnoreCase);
        var catalog = GetMetricCatalog();

        int order = 0;
        foreach (var item in catalog)
        {
            existing.TryGetValue(item.MetricId, out var cfg);
            _metricRows.Add(new MetricRow
            {
                MetricId = item.MetricId,
                DisplayName = item.DisplayName,
                Category = item.Category,
                Enabled = cfg?.Enabled ?? item.DefaultEnabled,
                DisplayOrder = cfg?.DisplayOrder ?? order,
                WarningThreshold = cfg?.WarningThreshold,
                CriticalThreshold = cfg?.CriticalThreshold
            });
            order++;
        }

        _metricRows.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        foreach (var mr in _metricRows)
        {
            int idx = _dgvMetrics.Rows.Add(
                mr.Enabled,
                mr.DisplayName,
                mr.Category,
                mr.WarningThreshold?.ToString("F1") ?? "",
                mr.CriticalThreshold?.ToString("F1") ?? "",
                mr.MetricId);
            _dgvMetrics.Rows[idx].Tag = mr;
        }
    }

    private static List<(string MetricId, string DisplayName, string Category, bool DefaultEnabled)> GetMetricCatalog() => new()
    {
        ("cpu.usage.total", "CPU Usage", "CPU", true),
        ("lhm.cpu.temperature.package", "CPU Package Temp (MSR)", "CPU", true),
        ("cpu.temperature.thermalzone", "CPU / ACPI Thermal Zone", "CPU", true),
        ("cpu.clock.current", "CPU Clock", "CPU", true),
        ("lhm.cpu.power", "CPU Power", "CPU", false),
        ("gpu.usage", "GPU Usage", "GPU", true),
        ("gpu.temperature", "GPU Temp", "GPU", true),
        ("gpu.clock", "GPU Core Clock", "GPU", false),
        ("gpu.memory.dedicated", "GPU Dedicated VRAM", "GPU", true),
        ("gpu.memory.shared", "GPU Shared Memory", "GPU", false),
        ("memory.usage.percent", "RAM Usage %", "Memory", true),
        ("memory.used", "RAM Used GB", "Memory", true),
        ("disk.usage.percent", "Disk Capacity %", "Storage", false),
        ("disk.read.speed", "Disk Read Speed", "Storage", false),
        ("disk.write.speed", "Disk Write Speed", "Storage", false),
        ("storage.temperature", "SSD Temperature", "Storage", false),
        ("cooling.fan.rpm", "Fan RPM (Read-Only)", "Cooling", false),
        ("game.fps", "FPS (PresentMon/ETW)", "Gaming", true),
        ("game.frametime", "Frame Time (ms)", "Gaming", true),
        ("game.fps.1percent_low", "1% Low FPS", "Gaming", false),
        ("network.ping.latency", "ICMP Ping Latency", "Network", true),
        ("network.ping.packetloss", "Network Probe Loss %", "Network", false),
        ("network.download.speed", "Download Speed", "Network", false),
        ("network.upload.speed", "Upload Speed", "Network", false)
    };

    private void DgvMetrics_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvMetrics.Rows.Count) return;
        var row = _dgvMetrics.Rows[e.RowIndex];
        if (row.Tag is not MetricRow mr) return;

        if (e.ColumnIndex == 0 && row.Cells[0].Value is bool enabled)
            mr.Enabled = enabled;
        else if (e.ColumnIndex == 3)
            mr.WarningThreshold = double.TryParse(row.Cells[3].Value?.ToString(), out var w) ? w : null;
        else if (e.ColumnIndex == 4)
            mr.CriticalThreshold = double.TryParse(row.Cells[4].Value?.ToString(), out var c) ? c : null;
    }

    private void MoveSelectedMetric(int delta)
    {
        if (_dgvMetrics.SelectedRows.Count == 0) return;
        int idx = _dgvMetrics.SelectedRows[0].Index;
        int target = idx + delta;
        if (target < 0 || target >= _dgvMetrics.Rows.Count) return;

        (_metricRows[idx], _metricRows[target]) = (_metricRows[target], _metricRows[idx]);
        for (int col = 0; col < _dgvMetrics.Columns.Count; col++)
        {
            var tmp = _dgvMetrics.Rows[idx].Cells[col].Value;
            _dgvMetrics.Rows[idx].Cells[col].Value = _dgvMetrics.Rows[target].Cells[col].Value;
            _dgvMetrics.Rows[target].Cells[col].Value = tmp;
        }
        var tagTmp = _dgvMetrics.Rows[idx].Tag;
        _dgvMetrics.Rows[idx].Tag = _dgvMetrics.Rows[target].Tag;
        _dgvMetrics.Rows[target].Tag = tagTmp;

        _dgvMetrics.ClearSelection();
        _dgvMetrics.Rows[target].Selected = true;
    }

    private void TestHotkeyRegistration()
    {
        if (_hotkeyManager is null) return;
        bool ok = _hotkeyManager.TryRegister(_hotkeyRecorder.RecordedModifiers, _hotkeyRecorder.RecordedKey);
        if (ok)
        {
            _lblHotkeyStatus.Text = $"Registered successfully: {HotkeyRecorder.FormatHotkey(_hotkeyRecorder.RecordedModifiers, _hotkeyRecorder.RecordedKey)}";
            _lblHotkeyStatus.ForeColor = Color.DarkGreen;
        }
        else
        {
            _lblHotkeyStatus.Text = _hotkeyManager.ConflictMessage ?? "Hotkey registration failed.";
            _lblHotkeyStatus.ForeColor = Color.Firebrick;
        }
    }

    private AppSettings BuildSettingsFromUi()
    {
        var next = _settings.Clone();
        next.LaunchAtStartup = _chkLaunchAtStartup.Checked;
        next.StartMinimized = _chkStartMinimized.Checked;
        next.StartOverlayVisible = _chkStartOverlayVisible.Checked;
        next.MonitoringMode = (MonitoringMode)Math.Clamp(_cboMonitoringMode.SelectedIndex, 0, 2);
        next.PollingIntervalMs = (int)_nudPollingMs.Value;
        next.DashboardTheme = _cboDashboardTheme.SelectedItem?.ToString() ?? "DarkGaming";
        next.ShowDashboardGraphs = _chkShowDashboardGraphs.Checked;
        next.ShowPerCoreCpu = _chkShowPerCoreCpu.Checked;
        next.ReducePollingOnBattery = _chkReduceOnBattery.Checked;
        next.PauseOnBattery = _chkPauseOnBattery.Checked;
        next.EnableWarningAlerts = _chkEnableAlerts.Checked;

        next.Hotkey.Modifiers = (int)_hotkeyRecorder.RecordedModifiers;
        next.Hotkey.Key = (int)_hotkeyRecorder.RecordedKey;

        next.Overlay.Position = _cboPosition.SelectedItem?.ToString() ?? "TopRight";
        next.Overlay.DisplayIndex = Math.Max(0, _cboDisplay.SelectedIndex);
        next.Overlay.FontSize = (int)_nudFontSize.Value;
        next.Overlay.FontFamily = _cboFontFamily.SelectedItem?.ToString() ?? "Segoe UI";
        next.Overlay.TextOpacity = _trkTextOpacity.Value / 100.0;
        next.Overlay.BackgroundOpacity = _trkBgOpacity.Value / 100.0;
        next.Overlay.ShowBackground = _chkShowBackground.Checked;
        next.Overlay.CompactSpacing = _chkCompactSpacing.Checked;
        next.Overlay.WarningHighlight = _chkWarningHighlight.Checked;
        next.Overlay.HighContrast = _chkHighContrast.Checked;
        next.Overlay.InteractiveMode = _chkInteractiveMode.Checked;
        next.Overlay.AutoHideSeconds = _nudAutoHide.Value > 0 ? (int)_nudAutoHide.Value : null;

        next.ShowUnavailableMetrics = _chkShowUnavailable.Checked;
        next.Metrics = _metricRows.Select((mr, i) => new MetricConfig
        {
            MetricId = mr.MetricId,
            Enabled = mr.Enabled,
            DisplayOrder = i,
            WarningThreshold = mr.WarningThreshold,
            CriticalThreshold = mr.CriticalThreshold
        }).ToList();

        next.GameProcesses = _lstGameProcesses.Items.Cast<string>()
            .Select(p => new GameProcessConfig { ProcessName = p, DisplayName = p })
            .ToList();
        next.EnableFpsCapture = _chkEnableFps.Checked;
        next.PingTarget = string.IsNullOrWhiteSpace(_txtPingTarget.Text) ? "8.8.8.8" : _txtPingTarget.Text.Trim();
        next.PingIntervalSeconds = (int)_nudPingInterval.Value;

        return next;
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        var updated = BuildSettingsFromUi();

        try
        {
            if (updated.LaunchAtStartup)
                StartupManager.EnableStartup(Application.ExecutablePath);
            else
                StartupManager.DisableStartup();

            await _settingsService.SaveAsync(updated);
            _settings = updated;
            SettingsSaved?.Invoke(this, updated);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnResetDefaults_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Reset all Carbon Monitor settings to defaults?", "Reset Settings",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _settings = await _settingsService.ResetToDefaultsAsync();
        PopulateFromSettings(_settings);
    }

    private async void BtnExport_Click(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "CarbonMonitor-Settings.json",
            DefaultExt = "json"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        await _settingsService.ExportAsync(dlg.FileName);
        MessageBox.Show("Settings exported.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void BtnImport_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _settings = await _settingsService.ImportAsync(dlg.FileName);
            PopulateFromSettings(_settings);
            MessageBox.Show("Settings imported. Review and click Save to apply.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 6, 8, 6)
    };

    private void PopulateDisplays()
    {
        _cboDisplay.Items.Clear();
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            string primary = screens[i].Primary ? " (Primary)" : "";
            _cboDisplay.Items.Add($"Display {i + 1}: {screens[i].Bounds.Width}x{screens[i].Bounds.Height}{primary}");
        }
        if (_cboDisplay.Items.Count > 0) _cboDisplay.SelectedIndex = 0;
    }

    private void SelectDisplayIndex(int index)
    {
        if (_cboDisplay.Items.Count == 0) return;
        _cboDisplay.SelectedIndex = index >= 0 && index < _cboDisplay.Items.Count ? index : 0;
    }

    private static void SelectComboItem(ComboBox combo, string value)
    {
        int idx = combo.Items.IndexOf(value);
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private sealed class MetricRow
    {
        public string MetricId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Enabled { get; set; }
        public int DisplayOrder { get; set; }
        public double? WarningThreshold { get; set; }
        public double? CriticalThreshold { get; set; }
    }
}
