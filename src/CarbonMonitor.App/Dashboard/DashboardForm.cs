using CarbonMonitor.App.Controls;
using CarbonMonitor.Core.Configuration;
using CarbonMonitor.Core.Models;
using CarbonMonitor.Core.Monitoring;
using CarbonMonitor.Infrastructure.Elevation;
using CarbonMonitor.Providers.Windows.Power;

namespace CarbonMonitor.App.Dashboard;

/// <summary>
/// Modern, responsive dark-gaming hardware monitoring dashboard.
/// Displays System Overview KPIs, CPU (with per-core bars & dual history graphs),
/// deduplicated physical GPU cards, Memory, Storage, and Gaming/Network/Cooling telemetry.
/// Stops UI timer updates and chart repaints while closed, hidden, or minimized.
/// </summary>
public sealed class DashboardForm : Form
{
    private readonly MonitoringEngine? _monitoringEngine;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private DashboardThemePalette _palette = DashboardThemePalette.DarkGaming;
    private AppSettings _settings = new();
    private DateTimeOffset _lastAppendedSampleTime = DateTimeOffset.MinValue;
    private bool _shouldClose;

    // Header controls
    private Panel _headerPanel = null!;
    private Label _lblAppTitle = null!;
    private Label _lblSystemSummary = null!;
    private Label _lblModePill = null!;
    private Label _lblPowerPill = null!;
    private Label _lblElevPill = null!;
    private Button _btnElevate = null!;
    private Button _btnOverlay = null!;
    private Button _btnSettings = null!;
    private Button _btnDiagnostics = null!;

    // Overview KPI strip
    private Panel _overviewContainer = null!;
    private KpiTileControl _kpiCpuTemp = null!;
    private KpiTileControl _kpiGpuTemp = null!;
    private KpiTileControl _kpiCpuLoad = null!;
    private KpiTileControl _kpiGpuLoad = null!;
    private KpiTileControl _kpiRamLoad = null!;
    private KpiTileControl _kpiFps = null!;

    // Main scroll & grid
    private Panel _scrollPanel = null!;
    private TableLayoutPanel _gridLayout = null!;

    // CPU card
    private DashboardCardPanel _cardCpu = null!;
    private Label _lblCpuModel = null!;
    private Label _lblCpuUsageVal = null!;
    private MetricBarControl _barCpuUsage = null!;
    private Label _lblCpuClock = null!;
    private Label _lblCpuPkgTemp = null!;
    private Label _lblCpuThermalZone = null!;
    private Label _lblCpuPower = null!;
    private Label _lblCpuSource = null!;
    private TableLayoutPanel _perCoreGrid = null!;
    private readonly List<(Label Label, MetricBarControl Bar)> _coreControls = new();
    private SparklineChart _chartCpuLoad = null!;
    private SparklineChart _chartCpuTemp = null!;

    // GPU container (deduplicated physical cards)
    private TableLayoutPanel _gpuStack = null!;
    private readonly List<GpuCardControls> _gpuCards = new();

    // Memory card
    private DashboardCardPanel _cardMemory = null!;
    private Label _lblMemUsageVal = null!;
    private MetricBarControl _barMemUsage = null!;
    private Label _lblMemDetails = null!;
    private Label _lblMemSource = null!;
    private SparklineChart _chartMemLoad = null!;

    // Storage card
    private DashboardCardPanel _cardStorage = null!;
    private Label _lblDiskModel = null!;
    private Label _lblDiskUsageVal = null!;
    private MetricBarControl _barDiskUsage = null!;
    private Label _lblDiskVolumes = null!;
    private Label _lblDiskThroughput = null!;
    private Label _lblDiskTemp = null!;
    private Label _lblDiskSource = null!;
    private SparklineChart _chartDiskActive = null!;

    // Gaming / Network / Cooling card
    private DashboardCardPanel _cardGaming = null!;
    private Label _lblGameFps = null!;
    private Label _lblGameFrameTime = null!;
    private Label _lblGameOnePercent = null!;
    private Label _lblGameStatus = null!;
    private Label _lblNetThroughput = null!;
    private Label _lblNetPing = null!;
    private Label _lblNetLoss = null!;
    private Label _lblCoolingFan = null!;
    private Label _lblLastUpdated = null!;

    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenDiagnosticsRequested;
    public event EventHandler? ElevateRequested;

    public DashboardForm(MonitoringEngine? monitoringEngine = null)
    {
        _monitoringEngine = monitoringEngine;
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _refreshTimer.Tick += (_, _) =>
        {
            if (Visible && WindowState != FormWindowState.Minimized)
                RefreshDisplay();
        };

        InitializeComponent();
        ApplyTheme(_palette);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Carbon Monitor - Dashboard";
        Size = new Size(1160, 820);
        MinimumSize = new Size(920, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        ShowInTaskbar = true;
        Icon = LoadAppIcon();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        BuildHeader();
        BuildOverviewStrip();
        BuildMainGrid();

        root.Controls.Add(_headerPanel, 0, 0);
        root.Controls.Add(_overviewContainer, 0, 1);
        root.Controls.Add(_scrollPanel, 0, 2);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private void BuildHeader()
    {
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(16, 10, 16, 8)
        };

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        _lblAppTitle = new Label
        {
            Text = "CARBON MONITOR",
            AutoSize = true,
            Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
            Margin = new Padding(0)
        };
        _lblSystemSummary = new Label
        {
            Text = "Initializing telemetry providers...",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0)
        };
        left.Controls.Add(_lblAppTitle);
        left.Controls.Add(_lblSystemSummary);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };

        _lblModePill = CreatePill("Mode: Always");
        _lblPowerPill = CreatePill("Power: --");
        _lblElevPill = CreatePill("Standard User");

        _btnElevate = CreateHeaderButton("Elevate Sensors", 118);
        _btnElevate.Visible = !ElevationHelper.IsProcessElevated();
        _btnElevate.Click += (_, _) => ElevateRequested?.Invoke(this, EventArgs.Empty);

        _btnOverlay = CreateHeaderButton("Overlay (Alt+R)", 114);
        _btnOverlay.Click += (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);

        _btnDiagnostics = CreateHeaderButton("Diagnostics", 95);
        _btnDiagnostics.Click += (_, _) => OpenDiagnosticsRequested?.Invoke(this, EventArgs.Empty);

        _btnSettings = CreateHeaderButton("Settings", 82);
        _btnSettings.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        right.Controls.AddRange(new Control[]
        {
            _lblModePill, _lblPowerPill, _lblElevPill,
            _btnElevate, _btnOverlay, _btnDiagnostics, _btnSettings
        });

        _headerPanel.Controls.Add(right);
        _headerPanel.Controls.Add(left);
    }

    private void BuildOverviewStrip()
    {
        _overviewContainer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(12, 6, 12, 4)
        };

        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1
        };
        for (int i = 0; i < 6; i++)
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666f));
        strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _kpiCpuTemp = new KpiTileControl("CPU Temp") { Dock = DockStyle.Fill };
        _kpiGpuTemp = new KpiTileControl("GPU Temp") { Dock = DockStyle.Fill };
        _kpiCpuLoad = new KpiTileControl("CPU Load") { Dock = DockStyle.Fill };
        _kpiGpuLoad = new KpiTileControl("GPU Load") { Dock = DockStyle.Fill };
        _kpiRamLoad = new KpiTileControl("RAM Used") { Dock = DockStyle.Fill };
        _kpiFps = new KpiTileControl("Gaming FPS") { Dock = DockStyle.Fill };

        strip.Controls.Add(_kpiCpuTemp, 0, 0);
        strip.Controls.Add(_kpiGpuTemp, 1, 0);
        strip.Controls.Add(_kpiCpuLoad, 2, 0);
        strip.Controls.Add(_kpiGpuLoad, 3, 0);
        strip.Controls.Add(_kpiRamLoad, 4, 0);
        strip.Controls.Add(_kpiFps, 5, 0);

        _overviewContainer.Controls.Add(strip);
    }

    private void BuildMainGrid()
    {
        _scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10, 4, 10, 10)
        };

        _gridLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3
        };
        _gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _gridLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _gridLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _gridLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        BuildCpuCard();
        BuildGpuContainer();
        BuildMemoryCard();
        BuildStorageCard();
        BuildGamingCard();

        _gridLayout.Controls.Add(_cardCpu, 0, 0);
        _gridLayout.Controls.Add(_gpuStack, 1, 0);
        _gridLayout.Controls.Add(_cardMemory, 0, 1);
        _gridLayout.Controls.Add(_cardStorage, 1, 1);
        _gridLayout.Controls.Add(_cardGaming, 0, 2);
        _gridLayout.SetColumnSpan(_cardGaming, 2);

        _scrollPanel.Controls.Add(_gridLayout);
    }

    private void BuildCpuCard()
    {
        _cardCpu = new DashboardCardPanel("CPU Telemetry")
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(420, 315)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        _lblCpuModel = CreateTitleValueLabel("Detecting processor...");
        layout.Controls.Add(_lblCpuModel, 0, 0);
        layout.SetColumnSpan(_lblCpuModel, 2);

        var usageHdr = CreateRowHeader("Overall Utilization", out _lblCpuUsageVal);
        layout.Controls.Add(usageHdr, 0, 1);
        layout.SetColumnSpan(usageHdr, 2);

        _barCpuUsage = new MetricBarControl { Dock = DockStyle.Top, Height = 10, Margin = new Padding(0, 2, 0, 8) };
        _barCpuUsage.SetThresholds(80, 95);
        layout.Controls.Add(_barCpuUsage, 0, 2);
        layout.SetColumnSpan(_barCpuUsage, 2);

        _lblCpuClock = CreateDetailLabel("Clock: --");
        _lblCpuPower = CreateDetailLabel("Package Power: --");
        layout.Controls.Add(_lblCpuClock, 0, 3);
        layout.Controls.Add(_lblCpuPower, 1, 3);

        _lblCpuPkgTemp = CreateDetailLabel("Package Temp (MSR): --");
        _lblCpuThermalZone = CreateDetailLabel("ACPI Thermal Zone: --");
        layout.Controls.Add(_lblCpuPkgTemp, 0, 4);
        layout.Controls.Add(_lblCpuThermalZone, 1, 4);

        _perCoreGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 6, 0, 6)
        };
        for (int c = 0; c < 4; c++)
            _perCoreGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.Controls.Add(_perCoreGrid, 0, 5);
        layout.SetColumnSpan(_perCoreGrid, 2);

        _chartCpuLoad = new SparklineChart { Dock = DockStyle.Top, Height = 64, Margin = new Padding(0, 2, 4, 4) };
        _chartCpuLoad.Configure("CPU Load History", "%", 0, 100);
        _chartCpuTemp = new SparklineChart { Dock = DockStyle.Top, Height = 64, Margin = new Padding(4, 2, 0, 4) };
        _chartCpuTemp.Configure("Thermal History", "°C", 25, 100);
        layout.Controls.Add(_chartCpuLoad, 0, 6);
        layout.Controls.Add(_chartCpuTemp, 1, 6);

        _lblCpuSource = CreateSourceLabel("Source: --");
        layout.Controls.Add(_lblCpuSource, 0, 7);
        layout.SetColumnSpan(_lblCpuSource, 2);

        _cardCpu.Body.Controls.Add(layout);
    }

    private void BuildGpuContainer()
    {
        _gpuStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0)
        };
        _gpuStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        AddGpuCard();
    }

    private GpuCardControls AddGpuCard()
    {
        var card = new DashboardCardPanel($"GPU {_gpuCards.Count}")
        {
            Dock = DockStyle.Top,
            Height = 315
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var g = new GpuCardControls { Card = card };
        g.Model = CreateTitleValueLabel("Detecting GPU...");
        layout.Controls.Add(g.Model, 0, 0);
        layout.SetColumnSpan(g.Model, 2);

        var usageHdr = CreateRowHeader("GPU Utilization", out var usageVal);
        g.UsageVal = usageVal;
        layout.Controls.Add(usageHdr, 0, 1);
        layout.SetColumnSpan(usageHdr, 2);

        g.UsageBar = new MetricBarControl { Dock = DockStyle.Top, Height = 10, Margin = new Padding(0, 2, 0, 8) };
        g.UsageBar.SetThresholds(85, 97);
        layout.Controls.Add(g.UsageBar, 0, 2);
        layout.SetColumnSpan(g.UsageBar, 2);

        g.Temperature = CreateDetailLabel("Temperature: --");
        g.Clock = CreateDetailLabel("Clock: --");
        layout.Controls.Add(g.Temperature, 0, 3);
        layout.Controls.Add(g.Clock, 1, 3);

        g.DedicatedVram = CreateDetailLabel("Dedicated VRAM: --");
        g.SharedMemory = CreateDetailLabel("Shared Memory: --");
        layout.Controls.Add(g.DedicatedVram, 0, 4);
        layout.Controls.Add(g.SharedMemory, 1, 4);

        g.Engines = CreateDetailLabel("Engines: --");
        layout.Controls.Add(g.Engines, 0, 5);
        layout.SetColumnSpan(g.Engines, 2);

        g.LoadChart = new SparklineChart { Dock = DockStyle.Top, Height = 64, Margin = new Padding(0, 4, 0, 4) };
        g.LoadChart.Configure("GPU Utilization History", "%", 0, 100);
        layout.Controls.Add(g.LoadChart, 0, 6);
        layout.SetColumnSpan(g.LoadChart, 2);

        var footer = new Panel { Dock = DockStyle.Top, Height = 20 };
        g.Source = CreateSourceLabel("Source: --");
        g.Source.Dock = DockStyle.Left;
        var lnkDiag = new LinkLabel
        {
            Text = "Adapter Diagnostics",
            Dock = DockStyle.Right,
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            LinkColor = Color.FromArgb(56, 189, 248)
        };
        lnkDiag.Click += (_, _) => OpenDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        footer.Controls.Add(lnkDiag);
        footer.Controls.Add(g.Source);
        layout.Controls.Add(footer, 0, 7);
        layout.SetColumnSpan(footer, 2);

        card.Body.Controls.Add(layout);
        card.ApplyTheme(_palette.GpuAccent, _palette);
        g.UsageBar.ApplyTheme(_palette.GpuAccent, _palette);
        g.LoadChart.ApplyTheme(_palette.GpuAccent, _palette);

        _gpuCards.Add(g);
        _gpuStack.RowCount = _gpuCards.Count;
        _gpuStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _gpuStack.Controls.Add(card, 0, _gpuCards.Count - 1);
        return g;
    }

    private void BuildMemoryCard()
    {
        _cardMemory = new DashboardCardPanel("Memory (RAM)")
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(420, 215)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = CreateRowHeader("Physical Memory Utilization", out _lblMemUsageVal);
        layout.Controls.Add(hdr, 0, 0);

        _barMemUsage = new MetricBarControl { Dock = DockStyle.Top, Height = 10, Margin = new Padding(0, 2, 0, 8) };
        _barMemUsage.SetThresholds(80, 93);
        layout.Controls.Add(_barMemUsage, 0, 1);

        _lblMemDetails = CreateDetailLabel("Used: -- GB  ·  Available: -- GB  ·  Total: -- GB");
        layout.Controls.Add(_lblMemDetails, 0, 2);

        _chartMemLoad = new SparklineChart { Dock = DockStyle.Top, Height = 64, Margin = new Padding(0, 6, 0, 4) };
        _chartMemLoad.Configure("Memory Usage History", "%", 0, 100);
        layout.Controls.Add(_chartMemLoad, 0, 3);

        _lblMemSource = CreateSourceLabel("Source: --");
        layout.Controls.Add(_lblMemSource, 0, 4);

        _cardMemory.Body.Controls.Add(layout);
    }

    private void BuildStorageCard()
    {
        _cardStorage = new DashboardCardPanel("Storage (NVMe / SATA)")
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(420, 215)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

        _lblDiskModel = CreateTitleValueLabel("Detecting drive...");
        layout.Controls.Add(_lblDiskModel, 0, 0);
        layout.SetColumnSpan(_lblDiskModel, 2);

        var hdr = CreateRowHeader("Capacity Used", out _lblDiskUsageVal);
        layout.Controls.Add(hdr, 0, 1);
        layout.SetColumnSpan(hdr, 2);

        _barDiskUsage = new MetricBarControl { Dock = DockStyle.Top, Height = 10, Margin = new Padding(0, 2, 0, 6) };
        _barDiskUsage.SetThresholds(85, 95);
        layout.Controls.Add(_barDiskUsage, 0, 2);
        layout.SetColumnSpan(_barDiskUsage, 2);

        _lblDiskThroughput = CreateDetailLabel("Read: -- MB/s  ·  Write: -- MB/s");
        _lblDiskTemp = CreateDetailLabel("SSD Temp: --");
        layout.Controls.Add(_lblDiskThroughput, 0, 3);
        layout.Controls.Add(_lblDiskTemp, 1, 3);

        _lblDiskVolumes = CreateDetailLabel("Volumes: --");
        layout.Controls.Add(_lblDiskVolumes, 0, 4);
        layout.SetColumnSpan(_lblDiskVolumes, 2);

        _chartDiskActive = new SparklineChart { Dock = DockStyle.Top, Height = 56, Margin = new Padding(0, 4, 0, 2) };
        _chartDiskActive.Configure("Disk Activity History", "%", 0, 100);
        layout.Controls.Add(_chartDiskActive, 0, 5);

        _lblDiskSource = CreateSourceLabel("Source: --");
        _lblDiskSource.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        layout.Controls.Add(_lblDiskSource, 1, 5);

        _cardStorage.Body.Controls.Add(layout);
    }

    private void BuildGamingCard()
    {
        _cardGaming = new DashboardCardPanel("Gaming Performance, Network & Cooling")
        {
            Dock = DockStyle.Top,
            Height = 148
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3
        };
        for (int i = 0; i < 4; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        _lblGameFps = CreateDetailLabel("FPS: -- (Disabled)");
        _lblGameFrameTime = CreateDetailLabel("Frame Time: --");
        _lblGameOnePercent = CreateDetailLabel("1% Low: --");
        _lblGameStatus = CreateDetailLabel("Game Target: Waiting");

        _lblNetThroughput = CreateDetailLabel("Network: --");
        _lblNetPing = CreateDetailLabel("ICMP Ping: --");
        _lblNetLoss = CreateDetailLabel("Probe Loss: --");
        _lblCoolingFan = CreateDetailLabel("Fan RPM: --");

        _lblLastUpdated = CreateSourceLabel("Last sample: --  ·  Overlay toggles via Alt+R independently of Dashboard");

        layout.Controls.Add(_lblGameFps, 0, 0);
        layout.Controls.Add(_lblGameFrameTime, 1, 0);
        layout.Controls.Add(_lblGameOnePercent, 2, 0);
        layout.Controls.Add(_lblGameStatus, 3, 0);

        layout.Controls.Add(_lblNetThroughput, 0, 1);
        layout.Controls.Add(_lblNetPing, 1, 1);
        layout.Controls.Add(_lblNetLoss, 2, 1);
        layout.Controls.Add(_lblCoolingFan, 3, 1);

        layout.Controls.Add(_lblLastUpdated, 0, 2);
        layout.SetColumnSpan(_lblLastUpdated, 4);

        _cardGaming.Body.Controls.Add(layout);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _refreshTimer.Interval = Math.Clamp(settings.PollingIntervalMs, 500, 10000);
        _btnOverlay.Text = $"Overlay ({settings.Hotkey.DisplayString})";

        bool showGraphs = settings.ShowDashboardGraphs;
        _chartCpuLoad.Visible = showGraphs;
        _chartCpuTemp.Visible = showGraphs;
        _chartMemLoad.Visible = showGraphs;
        _chartDiskActive.Visible = showGraphs;
        foreach (var gc in _gpuCards)
            gc.LoadChart.Visible = showGraphs;

        _perCoreGrid.Visible = settings.ShowPerCoreCpu;

        ApplyTheme(DashboardThemePalette.FromName(settings.DashboardTheme));
    }

    public void ApplyTheme(DashboardThemePalette palette)
    {
        _palette = palette;
        SuspendLayout();

        BackColor = palette.WindowBackground;
        _scrollPanel.BackColor = palette.WindowBackground;
        _overviewContainer.BackColor = palette.WindowBackground;
        _headerPanel.BackColor = palette.HeaderBackground;

        _lblAppTitle.ForeColor = palette.PrimaryText;
        _lblSystemSummary.ForeColor = palette.SecondaryText;

        StylePill(_lblModePill, palette.TileBackground, palette.SecondaryText);
        StylePill(_lblPowerPill, palette.TileBackground, palette.SecondaryText);
        bool elevated = ElevationHelper.IsProcessElevated();
        StylePill(_lblElevPill, palette.TileBackground, elevated ? palette.OkColor : palette.WarningColor);
        _lblElevPill.Text = elevated ? "Elevated Sensors Active" : "Standard User Mode";
        _btnElevate.Visible = !elevated;

        foreach (var btn in new[] { _btnElevate, _btnOverlay, _btnDiagnostics, _btnSettings })
        {
            btn.BackColor = palette.TileBackground;
            btn.ForeColor = palette.PrimaryText;
            btn.FlatAppearance.BorderColor = palette.CardBorder;
        }

        _kpiCpuTemp.ApplyTheme(palette.CpuAccent, palette);
        _kpiGpuTemp.ApplyTheme(palette.GpuAccent, palette);
        _kpiCpuLoad.ApplyTheme(palette.CpuAccent, palette);
        _kpiGpuLoad.ApplyTheme(palette.GpuAccent, palette);
        _kpiRamLoad.ApplyTheme(palette.MemoryAccent, palette);
        _kpiFps.ApplyTheme(palette.GamingAccent, palette);

        _cardCpu.ApplyTheme(palette.CpuAccent, palette);
        _barCpuUsage.ApplyTheme(palette.CpuAccent, palette);
        _chartCpuLoad.ApplyTheme(palette.CpuAccent, palette);
        _chartCpuTemp.ApplyTheme(palette.WarningColor, palette);

        foreach (var gc in _gpuCards)
        {
            gc.Card.ApplyTheme(palette.GpuAccent, palette);
            gc.UsageBar.ApplyTheme(palette.GpuAccent, palette);
            gc.LoadChart.ApplyTheme(palette.GpuAccent, palette);
            ApplyLabelsTheme(gc.Card.Body, palette);
        }

        _cardMemory.ApplyTheme(palette.MemoryAccent, palette);
        _barMemUsage.ApplyTheme(palette.MemoryAccent, palette);
        _chartMemLoad.ApplyTheme(palette.MemoryAccent, palette);

        _cardStorage.ApplyTheme(palette.StorageAccent, palette);
        _barDiskUsage.ApplyTheme(palette.StorageAccent, palette);
        _chartDiskActive.ApplyTheme(palette.StorageAccent, palette);

        _cardGaming.ApplyTheme(palette.GamingAccent, palette);

        ApplyLabelsTheme(_cardCpu.Body, palette);
        ApplyLabelsTheme(_cardMemory.Body, palette);
        ApplyLabelsTheme(_cardStorage.Body, palette);
        ApplyLabelsTheme(_cardGaming.Body, palette);

        ResumeLayout(true);
    }

    private static void ApplyLabelsTheme(Control root, DashboardThemePalette palette)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label lbl)
            {
                if (Equals(lbl.Tag, "title")) lbl.ForeColor = palette.PrimaryText;
                else if (Equals(lbl.Tag, "source")) lbl.ForeColor = palette.MutedText;
                else if (Equals(lbl.Tag, "val")) lbl.ForeColor = palette.PrimaryText;
                else lbl.ForeColor = palette.SecondaryText;
            }
            if (c.HasChildren)
                ApplyLabelsTheme(c, palette);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _refreshTimer.Start();
        RefreshDisplay();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateTimerState();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            if (!_refreshTimer.Enabled) _refreshTimer.Start();
            RefreshDisplay();
        }
        else
        {
            _refreshTimer.Stop();
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
        _refreshTimer.Stop();
        base.OnFormClosing(e);
    }

    public void ForceClose()
    {
        _shouldClose = true;
        Close();
    }

    public void SetProviderCount(int count) { /* Reflected in header summary */ }

    private void RefreshDisplay()
    {
        if (_monitoringEngine is null) return;

        var pwr = WindowsPowerStateProvider.GetCurrentPowerSource();
        var pct = WindowsPowerStateProvider.GetBatteryPercent();
        _lblModePill.Text = $"Mode: {_monitoringEngine.Mode} ({_monitoringEngine.State})";
        _lblPowerPill.Text = pct.HasValue ? $"{pwr} · {pct}%" : $"Power: {pwr}";

        var readings = _monitoringEngine.LatestReadings;
        if (readings.Count == 0) return;

        UpdateReadings(readings);
    }

    public void UpdateReadings(IReadOnlyList<SensorReading> readings)
    {
        if (!Visible || WindowState == FormWindowState.Minimized || readings.Count == 0)
            return;

        var lookup = new Dictionary<string, SensorReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in readings)
            lookup.TryAdd(r.MetricId, r);

        var gpus = _monitoringEngine?.GetAggregatedGpus()
                   ?? GpuReadingAggregator.Aggregate(readings, Array.Empty<HardwareInfo>());

        DateTimeOffset newestSample = readings.Max(r => r.Timestamp);
        bool appendHistory = newestSample > _lastAppendedSampleTime;
        if (appendHistory)
            _lastAppendedSampleTime = newestSample;

        UpdateCpuCard(readings, lookup, appendHistory);
        UpdateGpuCards(gpus, appendHistory);
        UpdateMemoryCard(lookup, appendHistory);
        UpdateStorageCard(lookup, appendHistory);
        UpdateGamingCard(lookup);
        UpdateOverviewTiles(lookup, gpus);

        var cpuModel = lookup.TryGetValue("cpu.usage.total", out var c) ? c.HardwareName : "CPU";
        var primaryGpu = gpus.FirstOrDefault(g => !g.IsVirtualOrSoftware)?.Name ?? "No GPU";
        _lblSystemSummary.Text = $"{cpuModel}  ·  {primaryGpu}";
    }

    private void UpdateOverviewTiles(Dictionary<string, SensorReading> lookup, IReadOnlyList<PhysicalGpuSnapshot> gpus)
    {
        // CPU Temp: prefer direct MSR package temp, fall back to ACPI thermal zone
        if (lookup.TryGetValue("lhm.cpu.temperature.package", out var pkgTemp) && pkgTemp.Availability == SensorAvailability.Available)
        {
            _kpiCpuTemp.UpdateTile(pkgTemp.DisplayValue, "Direct MSR Package", GetThermalColor(pkgTemp.Value));
        }
        else if (lookup.TryGetValue("cpu.temperature.thermalzone", out var tzTemp) && tzTemp.Availability == SensorAvailability.Available)
        {
            _kpiCpuTemp.UpdateTile(tzTemp.DisplayValue, "ACPI Thermal Zone", GetThermalColor(tzTemp.Value));
        }
        else
        {
            _kpiCpuTemp.UpdateTile("N/A", "Elevation needed", _palette.MutedText);
        }

        // GPU Temp
        var primaryGpu = gpus.FirstOrDefault(g => !g.IsVirtualOrSoftware);
        if (primaryGpu?.Temperature is { Availability: SensorAvailability.Available } gpuTemp)
        {
            _kpiGpuTemp.UpdateTile(gpuTemp.DisplayValue, primaryGpu.Vendor, GetThermalColor(gpuTemp.Value));
        }
        else
        {
            _kpiGpuTemp.UpdateTile("N/A", primaryGpu?.IsIntegrated == true ? "Not exposed (iGPU)" : "No sensor", _palette.MutedText);
        }

        // CPU Load
        if (lookup.TryGetValue("cpu.usage.total", out var cpuUsage) && cpuUsage.Availability == SensorAvailability.Available)
        {
            string clkSub = lookup.TryGetValue("cpu.clock.current", out var clk) && clk.Availability == SensorAvailability.Available
                ? clk.DisplayValue
                : "OS Counter";
            _kpiCpuLoad.UpdateTile(cpuUsage.DisplayValue, clkSub, GetLoadColor(cpuUsage.Value));
        }
        else
        {
            _kpiCpuLoad.UpdateTile("N/A", "Unavailable", _palette.MutedText);
        }

        // GPU Load
        if (primaryGpu?.Utilization is { Availability: SensorAvailability.Available } gpuUtil)
        {
            _kpiGpuLoad.UpdateTile(gpuUtil.DisplayValue, primaryGpu.IsIntegrated ? "Integrated" : "Dedicated", GetLoadColor(gpuUtil.Value));
        }
        else
        {
            _kpiGpuLoad.UpdateTile("N/A", "Unavailable", _palette.MutedText);
        }

        // RAM Load
        if (lookup.TryGetValue("memory.usage.percent", out var memPct) && memPct.Availability == SensorAvailability.Available)
        {
            string memSub = lookup.TryGetValue("memory.used", out var mu) && lookup.TryGetValue("memory.total", out var mt)
                ? $"{mu.DisplayValue} / {mt.DisplayValue}"
                : "Physical RAM";
            _kpiRamLoad.UpdateTile(memPct.DisplayValue, memSub, GetLoadColor(memPct.Value));
        }

        // FPS
        if (lookup.TryGetValue("game.fps", out var fps) && fps.Availability == SensorAvailability.Available)
        {
            string ftSub = lookup.TryGetValue("game.frametime", out var ft) && ft.Availability == SensorAvailability.Available
                ? $"{ft.DisplayValue} frame"
                : fps.HardwareName;
            _kpiFps.UpdateTile(fps.DisplayValue, ftSub, _palette.OkColor);
        }
        else
        {
            string sub = _settings.EnableFpsCapture ? "Waiting for game" : "Capture disabled";
            _kpiFps.UpdateTile("--", sub, _palette.MutedText);
        }
    }

    private void UpdateCpuCard(IReadOnlyList<SensorReading> readings, Dictionary<string, SensorReading> lookup, bool appendHistory)
    {
        if (lookup.TryGetValue("cpu.usage.total", out var usage))
        {
            _lblCpuModel.Text = string.IsNullOrWhiteSpace(usage.HardwareName) ? "Processor" : usage.HardwareName;
            if (usage.Availability == SensorAvailability.Available && usage.Value is double u)
            {
                _lblCpuUsageVal.Text = usage.DisplayValue;
                _barCpuUsage.Percentage = u;
                if (appendHistory) _chartCpuLoad.AddSample(u);
                SetCardActive(_cardCpu, "ACTIVE", _palette.OkColor);
            }
            else
            {
                _lblCpuUsageVal.Text = "N/A";
                _barCpuUsage.Percentage = 0;
                SetCardActive(_cardCpu, "LIMITED", _palette.WarningColor);
            }
        }

        _lblCpuClock.Text = lookup.TryGetValue("cpu.clock.current", out var clk) && clk.Availability == SensorAvailability.Available
            ? $"Clock: {clk.DisplayValue} ({(clk.Value!.Value / 1000.0):F2} GHz)"
            : "Clock: N/A";

        _lblCpuPower.Text = lookup.TryGetValue("lhm.cpu.power", out var pwr) && pwr.Availability == SensorAvailability.Available
            ? $"Package Power: {pwr.DisplayValue}"
            : "Package Power: Admin required (MSR)";

        double? thermalForChart = null;
        if (lookup.TryGetValue("lhm.cpu.temperature.package", out var pkg) && pkg.Availability == SensorAvailability.Available)
        {
            _lblCpuPkgTemp.Text = $"Package Temp (MSR): {pkg.DisplayValue}";
            thermalForChart = pkg.Value;
        }
        else
        {
            _lblCpuPkgTemp.Text = "Package Temp (MSR): Admin required";
        }

        if (lookup.TryGetValue("cpu.temperature.thermalzone", out var tz) && tz.Availability == SensorAvailability.Available)
        {
            _lblCpuThermalZone.Text = $"ACPI Thermal Zone: {tz.DisplayValue}";
            thermalForChart ??= tz.Value;
        }
        else
        {
            _lblCpuThermalZone.Text = "ACPI Thermal Zone: N/A";
        }

        if (appendHistory && thermalForChart.HasValue)
            _chartCpuTemp.AddSample(thermalForChart.Value);

        // Per-core bars
        var coreReadings = readings
            .Where(r => r.MetricId.StartsWith("cpu.core.", StringComparison.OrdinalIgnoreCase) && r.Availability == SensorAvailability.Available)
            .OrderBy(r => int.TryParse(r.MetricId.AsSpan("cpu.core.".Length), out var n) ? n : 999)
            .ToList();

        if (_settings.ShowPerCoreCpu && coreReadings.Count > 0)
            UpdatePerCoreGrid(coreReadings);

        var sources = new[] { "cpu.usage.total", "lhm.cpu.temperature.package", "cpu.temperature.thermalzone" }
            .Select(id => lookup.TryGetValue(id, out var sr) ? sr.ProviderName : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        _lblCpuSource.Text = $"Sources: {string.Join(", ", sources)}";
    }

    private void UpdatePerCoreGrid(List<SensorReading> coreReadings)
    {
        if (_coreControls.Count != coreReadings.Count)
        {
            _perCoreGrid.SuspendLayout();
            _perCoreGrid.Controls.Clear();
            _coreControls.Clear();

            int rows = (int)Math.Ceiling(coreReadings.Count / 4.0);
            _perCoreGrid.RowCount = rows;
            _perCoreGrid.RowStyles.Clear();
            for (int r = 0; r < rows; r++)
                _perCoreGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            for (int i = 0; i < coreReadings.Count; i++)
            {
                var cell = new Panel { Dock = DockStyle.Top, Height = 26, Margin = new Padding(2) };
                var lbl = new Label
                {
                    Text = $"T{i}: --%",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 7.5f),
                    ForeColor = _palette.SecondaryText
                };
                var bar = new MetricBarControl { Dock = DockStyle.Bottom, Height = 6 };
                bar.ApplyTheme(_palette.CpuAccent, _palette);
                cell.Controls.Add(bar);
                cell.Controls.Add(lbl);

                _coreControls.Add((lbl, bar));
                _perCoreGrid.Controls.Add(cell, i % 4, i / 4);
            }
            _perCoreGrid.ResumeLayout(true);
        }

        for (int i = 0; i < coreReadings.Count; i++)
        {
            double val = coreReadings[i].Value ?? 0;
            _coreControls[i].Label.Text = $"T{i}: {val:F0}%";
            _coreControls[i].Bar.Percentage = val;
        }
    }

    private void UpdateGpuCards(IReadOnlyList<PhysicalGpuSnapshot> allGpus, bool appendHistory)
    {
        var visibleGpus = allGpus.Where(g => !g.IsVirtualOrSoftware).ToList();

        if (visibleGpus.Count == 0 && allGpus.Count > 0)
            visibleGpus = allGpus.ToList();

        while (_gpuCards.Count < Math.Max(1, visibleGpus.Count))
            AddGpuCard();

        for (int i = 0; i < _gpuCards.Count; i++)
        {
            var gc = _gpuCards[i];
            if (i >= visibleGpus.Count)
            {
                gc.Card.Visible = false;
                continue;
            }

            var gpu = visibleGpus[i];
            gc.Card.Visible = true;
            string typeTag = gpu.IsIntegrated ? "Integrated" : "Dedicated";
            gc.Card.SetTitle($"GPU {i} · {typeTag}");
            gc.Model.Text = string.IsNullOrWhiteSpace(gpu.DriverVersion)
                ? gpu.Name
                : $"{gpu.Name}  (Driver {gpu.DriverVersion})";

            if (gpu.Utilization is { Availability: SensorAvailability.Available, Value: double u })
            {
                gc.UsageVal.Text = gpu.Utilization.DisplayValue;
                gc.UsageBar.Percentage = u;
                if (appendHistory) gc.LoadChart.AddSample(u);
                SetCardActive(gc.Card, gpu.Temperature is { Availability: SensorAvailability.Available } ? "ACTIVE" : "PARTIAL",
                    gpu.Temperature is { Availability: SensorAvailability.Available } ? _palette.OkColor : _palette.CpuAccent);
            }
            else
            {
                gc.UsageVal.Text = "N/A";
                gc.UsageBar.Percentage = 0;
                SetCardActive(gc.Card, "LIMITED", _palette.WarningColor);
            }

            gc.Temperature.Text = gpu.Temperature is { Availability: SensorAvailability.Available }
                ? $"Temperature: {gpu.Temperature.DisplayValue}"
                : $"Temperature: N/A ({(gpu.IsIntegrated ? "No discrete iGPU sensor" : "Not exposed")})";

            gc.Clock.Text = gpu.CoreClock is { Availability: SensorAvailability.Available }
                ? $"Core Clock: {gpu.CoreClock.DisplayValue}"
                : "Core Clock: N/A (Not exposed)";

            gc.DedicatedVram.Text = gpu.DedicatedVramUsed is { Availability: SensorAvailability.Available }
                ? (gpu.DedicatedVramTotal is { Availability: SensorAvailability.Available }
                    ? $"Dedicated VRAM: {gpu.DedicatedVramUsed.DisplayValue} / {gpu.DedicatedVramTotal.DisplayValue}"
                    : $"Dedicated VRAM: {gpu.DedicatedVramUsed.DisplayValue}")
                : (gpu.IsIntegrated ? "Dedicated VRAM: N/A (Uses shared RAM)" : "Dedicated VRAM: N/A");

            gc.SharedMemory.Text = gpu.SharedMemoryUsed is { Availability: SensorAvailability.Available }
                ? (gpu.SharedMemoryTotal is { Availability: SensorAvailability.Available }
                    ? $"Shared GPU Mem: {gpu.SharedMemoryUsed.DisplayValue} / {gpu.SharedMemoryTotal.DisplayValue}"
                    : $"Shared GPU Mem: {gpu.SharedMemoryUsed.DisplayValue}")
                : "Shared GPU Mem: N/A";

            // Engine loads summary (3D, VideoDecode, Copy)
            var engSummary = gpu.EngineReadings
                .Where(e => e.Availability == SensorAvailability.Available && e.Value.HasValue)
                .GroupBy(e => e.DisplayName.Replace(gpu.Name, "", StringComparison.OrdinalIgnoreCase).Trim())
                .Select(grp => $"{grp.Key}: {grp.First().Value:F1}%")
                .Take(4)
                .ToList();
            gc.Engines.Text = engSummary.Count > 0
                ? $"Engines: {string.Join("  ·  ", engSummary)}"
                : "Engines: Standard WDDM counters";

            gc.Source.Text = $"Sources: {string.Join(", ", gpu.ContributingProviders)}";
        }
    }

    private void UpdateMemoryCard(Dictionary<string, SensorReading> lookup, bool appendHistory)
    {
        if (lookup.TryGetValue("memory.usage.percent", out var pct) && pct.Availability == SensorAvailability.Available && pct.Value is double p)
        {
            _lblMemUsageVal.Text = pct.DisplayValue;
            _barMemUsage.Percentage = p;
            if (appendHistory) _chartMemLoad.AddSample(p);
            SetCardActive(_cardMemory, "ACTIVE", _palette.OkColor);
        }

        string used = lookup.TryGetValue("memory.used", out var u) ? u.DisplayValue : "--";
        string avail = lookup.TryGetValue("memory.available", out var a) ? a.DisplayValue : "--";
        string total = lookup.TryGetValue("memory.total", out var t) ? t.DisplayValue : "--";
        _lblMemDetails.Text = $"Used: {used}   ·   Available: {avail}   ·   Total: {total}";
        _lblMemSource.Text = $"Source: {lookup.GetValueOrDefault("memory.usage.percent")?.ProviderName ?? "--"}";
    }

    private void UpdateStorageCard(Dictionary<string, SensorReading> lookup, bool appendHistory)
    {
        string model = lookup.TryGetValue("disk.usage.percent", out var du) ? du.HardwareName : "Storage";
        string health = lookup.TryGetValue("storage.health.status", out var sh) && sh.Availability == SensorAvailability.Available
            ? sh.DisplayValue
            : "Status N/A";
        _lblDiskModel.Text = $"{model}  ·  {health}";

        if (du is { Availability: SensorAvailability.Available, Value: double pct })
        {
            string capText = lookup.TryGetValue("disk.used.gb", out var ugb) && lookup.TryGetValue("disk.total.gb", out var tgb)
                ? $"{ugb.DisplayValue} / {tgb.DisplayValue} ({pct:F1}%)"
                : du.DisplayValue;
            _lblDiskUsageVal.Text = capText;
            _barDiskUsage.Percentage = pct;
            SetCardActive(_cardStorage, "ACTIVE", _palette.OkColor);
        }

        string rSpeed = lookup.TryGetValue("disk.read.speed", out var rs) ? rs.DisplayValue : "--";
        string wSpeed = lookup.TryGetValue("disk.write.speed", out var ws) ? ws.DisplayValue : "--";
        string actPct = lookup.TryGetValue("disk.active.percent", out var ap) ? ap.DisplayValue : "--";
        _lblDiskThroughput.Text = $"R: {rSpeed}  ·  W: {wSpeed}  ·  Active: {actPct}";

        if (appendHistory && ap is { Availability: SensorAvailability.Available, Value: double actVal })
            _chartDiskActive.AddSample(actVal);

        _lblDiskTemp.Text = lookup.TryGetValue("storage.temperature", out var st) && st.Availability == SensorAvailability.Available
            ? $"SSD Temp: {st.DisplayValue}"
            : "SSD Temp: Admin required";

        _lblDiskVolumes.Text = lookup.TryGetValue("disk.volumes", out var vols) && vols.Availability == SensorAvailability.Available
            ? $"Volumes: {vols.DisplayValue}"
            : "Volumes: --";

        _lblDiskSource.Text = $"Sources: {du?.ProviderName ?? "--"}, {sh?.ProviderName ?? "--"}";
    }

    private void UpdateGamingCard(Dictionary<string, SensorReading> lookup)
    {
        bool fpsActive = lookup.TryGetValue("game.fps", out var fps) && fps.Availability == SensorAvailability.Available;
        SetCardActive(_cardGaming, fpsActive ? "CAPTURING" : "STANDBY", fpsActive ? _palette.OkColor : _palette.SecondaryText);

        _lblGameFps.Text = fpsActive
            ? $"FPS: {fps!.DisplayValue}"
            : $"FPS: N/A ({(_settings.EnableFpsCapture ? "Waiting for game" : "Disabled in Settings")})";

        _lblGameFrameTime.Text = lookup.TryGetValue("game.frametime", out var ft) && ft.Availability == SensorAvailability.Available
            ? $"Frame Time: {ft.DisplayValue}"
            : "Frame Time: --";

        _lblGameOnePercent.Text = lookup.TryGetValue("game.fps.1percent_low", out var low) && low.Availability == SensorAvailability.Available
            ? $"1% Low: {low.DisplayValue}"
            : "1% Low: --";

        _lblGameStatus.Text = fpsActive
            ? $"Target: {fps!.HardwareName} (ETW)"
            : $"Target: {fps?.UnavailableReason ?? "No active game"}";

        string dl = lookup.TryGetValue("network.download.speed", out var dlR) ? dlR.DisplayValue : "--";
        string ul = lookup.TryGetValue("network.upload.speed", out var ulR) ? ulR.DisplayValue : "--";
        _lblNetThroughput.Text = $"Net: ↓ {dl}  ·  ↑ {ul}";

        _lblNetPing.Text = lookup.TryGetValue("network.ping.latency", out var ping) && ping.Availability == SensorAvailability.Available
            ? $"ICMP Ping ({_settings.PingTarget}): {ping.DisplayValue}"
            : $"ICMP Ping ({_settings.PingTarget}): --";

        _lblNetLoss.Text = lookup.TryGetValue("network.ping.packetloss", out var pl) && pl.Availability == SensorAvailability.Available
            ? $"Network Probe Loss: {pl.DisplayValue}"
            : "Network Probe Loss: --";

        _lblCoolingFan.Text = lookup.TryGetValue("cooling.fan.rpm", out var fan) && fan.Availability == SensorAvailability.Available
            ? $"Fan RPM: {fan.DisplayValue}"
            : "Fan RPM: N/A (Requires elevated/EC sensor)";

        _lblLastUpdated.Text = $"Last sample: {DateTimeOffset.Now:HH:mm:ss}  ·  Overlay hotkey: {_settings.Hotkey.DisplayString} (works when Dashboard is closed)";
    }

    private void SetCardActive(DashboardCardPanel card, string text, Color fore)
        => card.SetBadge(text, fore, _palette.TileBackground);

    private Color GetThermalColor(double? temp) => temp switch
    {
        >= 92 => _palette.CriticalColor,
        >= 80 => _palette.WarningColor,
        _ => _palette.OkColor
    };

    private Color GetLoadColor(double? pct) => pct switch
    {
        >= 95 => _palette.CriticalColor,
        >= 82 => _palette.WarningColor,
        _ => _palette.PrimaryText
    };

    private static Label CreatePill(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        Padding = new Padding(8, 5, 8, 5),
        Margin = new Padding(0, 2, 6, 0)
    };

    private static void StylePill(Label pill, Color bg, Color fg)
    {
        pill.BackColor = bg;
        pill.ForeColor = fg;
    }

    private static Button CreateHeaderButton(string text, int width)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(2, 0, 4, 0),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    private static Label CreateTitleValueLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 6),
        Tag = "title"
    };

    private static Label CreateDetailLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 8.5f),
        Margin = new Padding(0, 2, 6, 2)
    };

    private static Label CreateSourceLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 7.5f),
        Margin = new Padding(0, 4, 0, 0),
        Tag = "source"
    };

    private static Panel CreateRowHeader(string leftText, out Label rightValueLabel)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 20, Margin = new Padding(0) };
        var left = new Label { Text = leftText, Dock = DockStyle.Left, AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
        rightValueLabel = new Label
        {
            Text = "--",
            Dock = DockStyle.Right,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Tag = "val"
        };
        panel.Controls.Add(rightValueLabel);
        panel.Controls.Add(left);
        return panel;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "carbon-monitor.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { }
        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class GpuCardControls
    {
        public DashboardCardPanel Card { get; set; } = null!;
        public Label Model { get; set; } = null!;
        public Label UsageVal { get; set; } = null!;
        public MetricBarControl UsageBar { get; set; } = null!;
        public Label Temperature { get; set; } = null!;
        public Label Clock { get; set; } = null!;
        public Label DedicatedVram { get; set; } = null!;
        public Label SharedMemory { get; set; } = null!;
        public Label Engines { get; set; } = null!;
        public SparklineChart LoadChart { get; set; } = null!;
        public Label Source { get; set; } = null!;
    }
}
