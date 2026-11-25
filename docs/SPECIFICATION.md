# Carbon Monitor --- Product Requirements & Technical Specification

**Document type:** Master Product Specification (PRD + Technical
Design)\
**Product:** Carbon Monitor\
**Target:** Windows 10/11 x64 gaming PCs and laptops\
**Primary reference device:** Lenovo ThinkPad X1 Carbon 6th Gen, Intel
Core i7-8650U, Intel UHD Graphics 620, 16 GB LPDDR3, Samsung NVMe SSD\
**Document status:** Baseline specification for implementation by a
coding agent\
**Version:** 1.0

------------------------------------------------------------------------

## 1. Product vision

Carbon Monitor is a lightweight, configurable Windows
hardware-monitoring utility for gamers. It provides a compact,
always-on-top, text-first overlay that can be toggled with a
user-configurable global keyboard shortcut. It also provides a normal
desktop GUI for hardware diagnostics, settings, and sensor selection.

The product must work across different Windows gaming computers,
including: - Intel integrated graphics (including older Intel UHD
generations such as UHD 620). - AMD integrated graphics. - NVIDIA
dedicated GPUs (including GeForce RTX 3060-class cards). - AMD dedicated
GPUs. - Hybrid graphics laptops with an integrated GPU and a dedicated
GPU. - Different CPU, RAM, motherboard, cooling, and NVMe/SATA storage
configurations.

**Core product principle:** broad hardware adaptability, with special
attention to systems whose integrated GPU or laptop firmware exposes
fewer sensors than a desktop GPU. Do not assume that a metric is
available merely because the hardware exists.

Carbon Monitor is not a game cheat, game-memory reader, or hardware
overclocking utility. It must not inject code into games, modify game
memory, bypass anti-cheat, or require a kernel driver for its core
monitoring features.

## 2. Primary user problem

Some users can use vendor tools or MSI Afterburner/RivaTuner, but those
tools may not expose all desired readings on older integrated-GPU
laptops, may have compatibility problems with anti-cheat software, or
may be too heavy/complex for low-power systems.

The primary reference user plays Valorant on a Lenovo ThinkPad X1 Carbon
6th Gen with: - Intel Core i7-8650U, 4 cores / 8 threads. - Intel UHD
Graphics 620 integrated graphics. - 16 GB LPDDR3 memory (soldered on
this model; do not assume two upgradeable RAM slots). - Samsung NVMe
SSD. - Windows 11.

The application must be validated on this reference device first, while
remaining reusable for other PCs.

## 3. Product goals

1.  Provide trustworthy live hardware telemetry.
2.  Display selected readings in a compact top-right overlay.
3.  Toggle the overlay with a configurable global shortcut; default
    `Alt + R`.
4.  Start automatically with Windows only when the user enables that
    setting.
5.  Offer monitoring modes:
    -   Always monitor (default).
    -   Monitor only while a selected game is running (Valorant is a
        preset).
    -   Monitor only while the overlay is visible (optional low-activity
        mode).
6.  Allow users to select exactly which metrics appear in the overlay.
7.  Allow users to customize overlay position, text size, opacity,
    spacing, and refresh interval within safe bounds.
8.  Provide a GUI dashboard and a hardware/sensor diagnostics view.
9.  Package the app as a normal installable Windows application. End
    users must not need VS Code, Visual Studio, Python, or the .NET SDK.
10. Keep idle CPU and memory usage low, and measure actual resource use
    rather than promising unverified numbers.
11. Report unavailable readings honestly as `N/A` / `Unsupported` with a
    reason where possible.
12. Support Intel, NVIDIA, and AMD hardware through provider adapters
    rather than a single vendor-specific implementation.

## 4. Non-goals and safety boundaries

Not included in the initial product: - Manual fan control or Embedded
Controller (EC) register writes. - CPU/GPU overclocking, undervolting,
power-limit changes, or voltage control. - Kernel drivers or custom
low-level drivers. - DLL injection, process memory reading/writing, game
hooks, or anti-cheat bypass. - Guaranteed Vanguard compatibility.
Compatibility must be tested; do not claim that an external overlay is
guaranteed to be allowed. - Fabricating GPU temperature by relabeling
CPU package temperature. - Treating a generic ping test as authoritative
Valorant game-server packet loss. - Cloud accounts, telemetry uploads,
advertisements, or analytics by default.

Fan RPM may be read if a supported, safe sensor provider exposes it.
Manual fan control is explicitly out of scope until a separate
model-specific safety and compatibility review is completed.

## 5. Target platform and distribution

### 5.1 Runtime

-   Windows 10 and Windows 11, x64.
-   Prefer a supported long-term .NET release at implementation time;
    current baseline: .NET 10 LTS, subject to verified SDK/runtime
    support and dependency compatibility.
-   Build as a self-contained, framework-dependent-free Windows x64
    deployment so end users do not need to install .NET separately.
-   Do not bundle development tools.

### 5.2 Installer

-   Use Inno Setup (or another mature Windows installer if the coding
    agent gives a technically justified reason).
-   Produce `CarbonMonitor-Setup-x64.exe`.
-   Installer options:
    -   Install for current user by default; offer all-users install
        only if required and properly permissioned.
    -   Start Carbon Monitor after installation (checkbox).
    -   Launch at Windows sign-in (checkbox, default configurable; do
        not silently enable).
    -   Create Start Menu shortcut.
    -   Optional desktop shortcut.
-   Include a proper uninstaller that removes app files and the app's
    own startup registration.
-   Preserve user settings on upgrade where appropriate; explain whether
    settings are retained on uninstall.
-   Sign binaries/installer if a signing certificate is available. Do
    not imply an unsigned installer is signed.
-   Include third-party license notices and comply with each
    dependency's license and redistribution terms.

## 6. User experience and application surfaces

### 6.1 System tray

The app should normally run quietly in the Windows notification area. -
Tray icon and context menu: - Open Dashboard - Show/Hide Overlay -
Settings - Hardware Diagnostics - Pause/Resume Monitoring - Exit -
Single application instance. A second launch should focus the existing
instance rather than create another monitoring engine. - Clear status
indicator for monitoring active/paused and overlay visible/hidden.

### 6.2 Dashboard

A standard GUI window, not continuously rendered while gaming. It should
contain: - Overview cards or rows for CPU, GPU(s), memory, storage,
network, and gaming telemetry. - Detected hardware names. - Current
value, unit, provider/source, and sensor availability. - Sensor status
such as Available, Unsupported, Permission Required, Temporarily
Unavailable, or Error. - Basic history graphs may be included, but must
not run expensive rendering while the dashboard is closed. - A
diagnostics page showing all detected sensors and provider status.

### 6.3 Compact gaming overlay

Default design: - Top-right corner of the primary gaming display. -
Text-first, compact, transparent/low-opacity background. - No large
charts, animations, video, or GPU-heavy rendering. - Always-on-top while
enabled. - Click-through by default so it does not intercept game input;
provide an optional interactive mode when configuring it outside the
game. - Overlay is hidden at startup until toggled, unless the user
explicitly configures otherwise. - Overlay must not steal focus from the
game. - Support multiple displays and remember selected
display/position. - Respect DPI scaling and different resolutions. - If
the game is exclusive fullscreen and Windows does not show the overlay,
explain this limitation and recommend borderless/windowed mode; do not
use injection to force it.

Default overlay fields (all individually configurable): - CPU usage -
CPU temperature / package temperature (only if accurately identified) -
CPU clock - GPU usage per adapter - GPU temperature per adapter, if
exposed - GPU clock, if exposed - Dedicated GPU memory usage / shared
GPU memory usage, with correct labels - RAM used / total / percentage -
Disk activity (read/write throughput or active time) - SSD temperature,
if exposed - Fan RPM, if exposed - FPS - Frame time - 1% low FPS if
reliable and supported - Ping/latency - Packet loss, with clear
source/method label

Do not display a metric by default if it is unsupported or misleading.
Allow users to hide unsupported metrics automatically or show them as
`N/A`.

### 6.4 Settings GUI

Use a clear native Windows GUI. Settings should be accessible without
editing JSON or restarting the PC.

**General** - Launch at Windows sign-in: On/Off. - Start minimized to
tray: On/Off. - Start overlay visible: On/Off (default Off). - Start
monitoring mode selector. - Run on battery / AC behavior (optional; safe
defaults).

**Monitoring mode** - Always monitor. - Only while selected game process
is running. - Only while overlay is visible. - Game selection should be
configurable by executable/process name. Valorant can be a preset, but
detection must be verified and configurable. Do not assume one process
name is universal across Riot client/game components.

**Hotkey** - Default: `Alt + R`. - User can record a new shortcut
through a key-capture control. - Support combinations such as
`Ctrl + Shift + F10`, subject to Windows API modifier/key limitations. -
Use Windows `RegisterHotKey` rather than global low-level keyboard
hooks. - Detect registration failure/conflicts and show a useful error;
never silently claim the shortcut is active. - Allow reset to default
and provide a way to recover if the chosen shortcut conflicts. - Do not
register shortcuts that are unsafe or unusable without modifiers.

**Overlay** - Show/hide. - Position: top-right, top-left, bottom-right,
bottom-left, custom drag position. - Display selection for multi-monitor
setups. - Font size and font family (use safe system fonts). -
Text/background opacity. - Compact spacing. - Optional background
panel. - Optional auto-hide timeout. - Optional warning highlighting. -
Preview overlay in settings.

**Metrics** - Individual checkbox for every metric. - Reorder metrics. -
Per-metric units/format where applicable. - Per-metric warning
thresholds where meaningful. - Show/hide unsupported metrics. - Group by
CPU/GPU/Memory/Storage/Network/Game.

**Diagnostics and privacy** - Export a local diagnostic report. -
Explain sensor source and why a reading is unavailable. - No automatic
upload of diagnostic reports. - Warn users to review reports for serial
numbers, usernames, machine identifiers, or other personal information
before sharing.

### 6.5 Settings persistence

-   Store settings in a versioned JSON file under the current user's
    LocalAppData application directory.
-   Validate and migrate configuration versions.
-   Write settings atomically (temporary file then replace) to avoid
    corruption.
-   Provide Reset to Defaults and Export/Import Settings.
-   Never store credentials or personal secrets.

## 7. Technical architecture

Use a modular architecture. Do not put all logic in the UI or in one
large `Program.cs`.

Suggested solution:

``` text
CarbonMonitor/
  CarbonMonitor.sln
  src/
    CarbonMonitor.App/
      Program.cs
      MainForm.cs
      Tray/
      Overlay/
      Settings/
      Dashboard/
      Diagnostics/
    CarbonMonitor.Core/
      Models/
      Abstractions/
      Monitoring/
      Configuration/
      Scheduling/
    CarbonMonitor.Providers.Windows/
      Cpu/
      Memory/
      Disk/
      Network/
      Gpu/
      Game/
    CarbonMonitor.Providers.Hardware/
      LibreHardwareMonitor/
      Intel/
      Nvidia/
      Amd/
      Nvme/
      ThinkPad/
    CarbonMonitor.Gaming/
      ProcessDetection/
      PresentMon/
    CarbonMonitor.Infrastructure/
      Logging/
      SingleInstance/
      Startup/
      SettingsStorage/
  installer/
    CarbonMonitor.iss
  tests/
    CarbonMonitor.Core.Tests/
    CarbonMonitor.Providers.Tests/
  docs/
    THIRD-PARTY-NOTICES.md
    BUILDING.md
```

### 7.1 Technology choices

-   Language: C#.
-   Desktop GUI: WinForms for a lightweight native Windows experience.
    WPF is acceptable only if justified by a concrete requirement and
    measured overhead.
-   Framework: .NET 10 LTS baseline; verify package compatibility.
-   Hardware abstraction: provider interfaces and a common sensor model.
-   Settings: `System.Text.Json`.
-   Global hotkey: Win32 `RegisterHotKey` / `UnregisterHotKey`.
-   Single-instance behavior: named mutex plus safe activation
    IPC/message.
-   Startup: per-user Startup folder shortcut or documented Run key;
    prefer the least-privilege approach and remove only Carbon Monitor's
    own entry.
-   Installer: Inno Setup.
-   Logging: lightweight rolling local logs with retention limits; no
    network logging.
-   Tests: unit tests for core logic/configuration and provider tests
    using mock data.

### 7.2 Common sensor model

Every metric should include: - Stable metric identifier. - Display
name. - Value and unit. - Sensor category (CPU, GPU, Memory, Storage,
Network, Gaming, Cooling). - Hardware/adapter identifier and friendly
name. - Provider/source name. - Timestamp and sample age. -
Availability/status. - Optional confidence/quality note. - Whether it is
a measured value, OS counter, or estimate.

Never silently merge two different sensors into one label. For example,
CPU package temperature must not be displayed as GPU temperature.

### 7.3 Provider interfaces

Define provider contracts similar to: - `IHardwareProvider` -
`ISensorProvider` - `IGpuTelemetryProvider` -
`IStorageTelemetryProvider` - `INetworkTelemetryProvider` -
`IGameTelemetryProvider`

Providers should: - Detect supported hardware and capabilities. - Return
available sensors. - Handle exceptions and timeouts. - Avoid crashing
the whole app if one provider fails. - Report provider health and reason
for missing data. - Avoid duplicate readings by using stable hardware
and sensor identifiers. - Be independently testable.

### 7.4 Provider strategy

Use a layered, capability-driven strategy rather than a hard-coded
vendor switch.

**General hardware** - Evaluate LibreHardwareMonitor for supported
sensors. - Check its current license and redistribution requirements
before bundling. - Run hardware access at the minimum required
privilege. If elevated access is required for a sensor, document it and
degrade gracefully when not elevated. - Do not run multiple competing
sensor libraries against the same hardware if they may conflict.

**Windows** - Windows Performance Counters / PDH for CPU, memory, disk
and GPU Engine utilization where available. - Windows APIs for adapter
enumeration, process detection, display and power state. - Windows GPU
Engine counters are not the same as a dedicated GPU temperature sensor.

**NVIDIA** - Use NVIDIA NVML where available and legally/technically
redistributable under its terms. - Detect driver/runtime availability. -
Retrieve supported temperature, utilization, clock, memory, power and
fan metrics. - Do not require NVML for basic app operation. - Do not
assume every NVIDIA GPU exposes every metric.

**AMD** - Evaluate AMD ADLX and its supported telemetry capabilities and
redistribution terms. - Gracefully handle unsupported models/drivers.

**Intel** - Use Windows GPU Engine counters for utilization where
supported. - Investigate Intel-supported telemetry interfaces for GPU
readings. - For older Intel UHD 620 systems, do not promise a separate
GPU temperature sensor. CPU package/core temperatures may be available,
but must retain their true labels. - Use CPU thermal sensors only where
actually exposed and validated.

**Storage** - Use Windows storage APIs / SMART / NVMe health interfaces
where accessible. - Distinguish SSD temperature, health, wear, and
activity. - If temperature cannot be retrieved, show unavailable rather
than substituting another sensor.

**Cooling** - Read fan RPM only through a supported, safe provider. -
For ThinkPad-specific readings, investigate documented/model-specific
interfaces. - No EC writes, fan-level changes, or manual fan curves in
this product baseline.

## 8. Gaming telemetry and anti-cheat compatibility

### 8.1 FPS and frame time

-   Evaluate PresentMon or a supported ETW-based telemetry method.
-   Use a documented external telemetry approach.
-   Do not inject DLLs, hook game functions, read game memory, or modify
    game processes.
-   Measure FPS/frame time only where the capture method and game are
    supported.
-   Make the capture method optional and disable it if it causes
    instability or anti-cheat warnings.
-   Clearly state that no overlay/capture method can be guaranteed
    compatible with every anti-cheat version.

### 8.2 Valorant

-   Test on a non-competitive/private environment first where possible.
-   Do not bypass Vanguard or attempt to hide the application from
    anti-cheat.
-   Do not use RivaTuner as a required dependency.
-   If Vanguard blocks the overlay or telemetry method, fail safely and
    offer an external monitoring mode (dashboard/tray) rather than
    trying to evade the restriction.
-   Never claim Vanguard compatibility until it has been tested with the
    current game and anti-cheat versions.

### 8.3 Network

-   Provide general ping/latency to a configurable endpoint.
-   Generic ICMP ping loss is not the same as Valorant server packet
    loss.
-   If the game exposes official network statistics through a supported
    UI/API, investigate that route; otherwise label the value as
    `Network probe packet loss`, not `Valorant packet loss`.
-   Avoid frequent probes or excessive network traffic.

## 9. Performance and resource requirements

The application is intended for low-power laptops, including an
8th-generation mobile Intel CPU with integrated graphics.

Design requirements: - No continuous dashboard rendering when the
dashboard is closed. - Overlay should render only when visible and only
when values change or on a low-frequency refresh. - Avoid animations and
expensive visual effects. - Use bounded polling intervals and adaptive
sampling. - Separate slow sensor reads from fast UI updates. - Avoid
creating a new thread/task per sensor per poll. - Use cancellation
tokens and bounded queues. - Cache hardware topology and refresh it
infrequently. - Pause or reduce nonessential sampling when the machine
is on battery, if configured. - Do not run multiple instances. - Log
only errors and essential lifecycle events by default.

**Initial targets to measure, not guaranteed claims:** - Idle CPU usage
target: below 0.5% average on reference hardware after warm-up. -
Working set target: below 50 MB for basic monitoring/hidden overlay, if
feasible with chosen providers. - Default sensor refresh: approximately
2--3 seconds; allow provider-specific intervals. - UI overlay refresh:
approximately 2--4 times per second at most, with no expensive redraw if
content is unchanged. - No sustained high CPU usage while hidden. -
Benchmark with each provider enabled/disabled and document results.

If the resource targets cannot be met, profile and optimize rather than
hiding the results or making unsupported claims.

## 10. Alerts and thermal information

-   User-configurable warning thresholds.
-   Separate warning levels (for example, informational, warning,
    critical).
-   Use CPU/GPU manufacturer specifications and device capabilities when
    setting defaults.
-   Do not treat one generic temperature threshold as safe for every
    processor/GPU.
-   Alerts should be unobtrusive and rate-limited.
-   Avoid repeated notifications for the same ongoing condition.
-   Display thermal throttling only when a reliable source exposes it.
-   Never automatically change fan speed or power settings.

## 11. Security, privacy, and reliability

-   No kernel driver required for baseline features.
-   No game process injection or memory manipulation.
-   No network telemetry or analytics by default.
-   Local logs only, with retention limits.
-   Validate configuration input.
-   Avoid requiring administrator privileges for normal operation;
    request elevation only for a clearly explained optional sensor
    capability.
-   Do not silently add startup persistence; the user must opt in.
-   Use secure update/install practices if an updater is added in a
    future version.
-   Graceful shutdown and disposal of sensor handles.
-   Provider failures must not crash the app.
-   Detect missing dependencies and show clear diagnostic messages.
-   Never claim a metric is live if its timestamp is stale.

## 12. Accessibility and usability

-   Clear units and labels.
-   High contrast option.
-   Adjustable font size.
-   Keyboard-accessible settings.
-   Tooltips explaining sensor sources and limitations.
-   Support Windows display scaling.
-   No reliance on color alone to communicate warnings.

## 13. Testing and acceptance criteria

The coding agent must implement automated tests where possible and
provide a manual test checklist.

### Functional acceptance

1.  Installer installs on a clean Windows 10/11 x64 machine without VS
    Code, Python, or .NET SDK.
2.  App launches and displays a tray icon.
3.  Only one instance can run.
4.  Default `Alt + R` toggles overlay when available.
5.  User can record and save another valid global hotkey.
6.  Hotkey conflicts/registration failures are clearly reported.
7.  Startup setting can be enabled and disabled from GUI.
8.  Monitoring mode can be switched between Always, selected game, and
    overlay-visible.
9.  User can enable/disable each overlay metric.
10. Settings persist after restart and upgrade.
11. Overlay position and appearance persist.
12. Unsupported metrics are marked unavailable, not fabricated.
13. A failed provider does not crash other providers.
14. Diagnostics can be exported locally.
15. Uninstaller removes app files and Carbon Monitor's own startup
    entry.

### Hardware validation matrix

Test and document: - Intel UHD 620 reference laptop (i7-8650U / X1
Carbon 6th Gen). - A newer Intel integrated GPU if available. - NVIDIA
GeForce RTX 3060 or comparable GPU. - AMD integrated graphics if
available. - AMD dedicated GPU if available. - Hybrid Intel + NVIDIA
laptop if available. - NVMe and SATA storage where available.

For each test, record: - OS build. - CPU/GPU model. - Driver versions. -
Detected sensors. - Missing sensors and reasons. - CPU/RAM use while
idle and while overlay is visible. - Whether gaming telemetry works. -
Any anti-cheat interaction or warnings.

### Performance acceptance

-   Measure process CPU and working set for at least 10 minutes idle
    with overlay hidden.
-   Measure for at least 10 minutes with overlay visible.
-   Measure during a game session if safe and permitted.
-   Compare with the app closed.
-   Report averages and peaks; do not cherry-pick.
-   No repeated polling spikes or memory growth over the test period.

### Anti-cheat acceptance

-   Do not test by bypassing or disabling anti-cheat.
-   If the game blocks the overlay, document the result and leave the
    safe fallback enabled.
-   Do not ship a feature that requires evading anti-cheat.

## 14. Build and delivery requirements

The coding agent must deliver: 1. Complete source repository with
solution and projects. 2. Clear README with prerequisites for developers
only. 3. Build instructions for VS Code or Visual Studio. 4. A
reproducible publish script. 5. Inno Setup installer script. 6.
Third-party notices and license review. 7. Automated tests. 8. Hardware
diagnostics/export functionality. 9. A self-contained x64 application
build. 10. `CarbonMonitor-Setup-x64.exe` installer when build tools are
available. 11. A release checklist and known limitations. 12. No
placeholder code presented as completed functionality.

If the agent's environment cannot compile Windows desktop software, it
must still provide a complete, coherent source project and exact Windows
build instructions, clearly stating that the installer has not been
compiled or tested. It must not fabricate build/test results.

## 15. Recommended implementation order (within one continuous project)

This is an implementation dependency order, not separate product
phases: 1. Create solution, core models, provider interfaces, settings
schema and tests. 2. Implement Windows baseline providers and hardware
discovery. 3. Integrate hardware sensor provider(s) with licensing
review and graceful fallback. 4. Implement tray app, single-instance
behavior, global hotkey and overlay. 5. Implement settings GUI and
metric customization. 6. Implement game process detection and optional
PresentMon integration. 7. Implement installer, startup preference,
uninstaller and packaging. 8. Run hardware, performance, and anti-cheat
compatibility tests. 9. Fix issues and produce a release candidate.

The product should be delivered as a coherent application, not as
disconnected demo scripts.

## 16. Open technical decisions the agent must verify

The coding agent must verify current documentation and dependency
versions rather than guessing: - .NET 10 compatibility with selected
hardware libraries and Windows versions. - Current LibreHardwareMonitor
license and redistribution conditions. - NVIDIA NVML redistribution and
runtime requirements. - AMD ADLX availability, licensing, and supported
hardware. - PresentMon integration method and license. - Windows 10
minimum supported build. - Whether a requested sensor is available on
Intel UHD 620 and the exact X1 Carbon firmware. - Whether a topmost
external overlay is displayed and permitted with current
Valorant/Vanguard. - Whether a given sensor library requires elevation
and what least-privilege alternatives exist.

Document all limitations. Prefer reliable partial telemetry over broad
but inaccurate claims.

------------------------------------------------------------------------

# Definition of done

Carbon Monitor is ready for its first public test release only when: - A
normal user can install it from a setup EXE without developer tools. -
The app runs in the tray and can be configured through a GUI. - The
overlay can be toggled and customized. - Hardware readings are sourced,
timestamped, and accurately labeled. - Intel integrated graphics are
specifically tested on the reference X1 Carbon. - NVIDIA and AMD
provider paths are implemented or clearly identified as pending, not
falsely advertised as tested. - Resource usage has been measured on the
reference laptop. - Anti-cheat limitations are documented and no bypass
techniques are used. - Installer, uninstaller, settings persistence, and
diagnostics work.
