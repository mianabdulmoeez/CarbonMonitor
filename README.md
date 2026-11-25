# Carbon Monitor

[![Platform: Windows 10 / 11 x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows&logoColor=white)](https://github.com/mianabdulmoeez/CarbonMonitor)
[![Target: .NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Tests: 200 Passing](https://img.shields.io/badge/Tests-200%20Passed-brightgreen.svg)](tests/)

**High-performance, lightweight Windows 10/11 x64 hardware telemetry utility and gaming overlay.**

Carbon Monitor runs quietly in the Windows system tray and provides:
- A compact, always-on-top, click-through text overlay toggled by a configurable global shortcut (default `Alt + R`).
- A native WinForms desktop dashboard and hardware/sensor diagnostics console.
- Adaptive, capability-driven sensor providers for Intel integrated graphics (referenced on Lenovo ThinkPad X1 Carbon 6th Gen, i7-8650U / UHD 620), NVIDIA dedicated GPUs, AMD Radeon GPUs, CPU, RAM, storage, cooling, network, and optional ETW frame telemetry.

---

## Features

- **Compact Text-First Overlay** — Top-right default placement, multi-monitor selection, DPI scaling (`PerMonitorV2`), click-through by default, optional interactive drag mode, high-contrast mode, and accessible `[!]` / `[!!]` warning indicators.
- **Global Hotkey (`RegisterHotKey`)** — Default `Alt + R`, customizable via a key-capture control with Win32 conflict detection (`ERROR_HOTKEY_ALREADY_REGISTERED`) and recovery/reset. Uses `RegisterHotKey` rather than a low-level keyboard hook.
- **Monitoring Modes** — Switch between `Always`, `Selected Game Only` (with `VALORANT-Win64-Shipping` preset), and `Overlay Visible Only`, plus battery-aware adaptive polling.
- **Honest Telemetry** — Never relabels CPU package temperature as GPU temperature, distinguishes dedicated VRAM from shared GPU memory, and reports unsupported sensors as `N/A` / `Unsupported` with explanations.
- **Optional External Frame Telemetry** — Supports external Intel `PresentMon.exe` CLI or `Microsoft.Diagnostics.Tracing.TraceEvent` DXGI ETW capture without DLL injection, game hooks, or memory reading.
- **Local Privacy & Persistence** — Versioned atomic JSON settings under `%LocalAppData%\CarbonMonitor\settings.json`, bounded rolling logs under `%LocalAppData%\CarbonMonitor\logs\`, local-only diagnostic export, and zero cloud analytics or ads.
- **Opt-In Windows Startup** — Configured only by explicit user opt-in in Settings or the installer; uninstaller removes only Carbon Monitor's own `HKCU` Run key.

---

## System Requirements

| Requirement | Details |
|---|---|
| **OS** | Windows 10 (1709+ recommended for GPU Engine counters) or Windows 11, x64 |
| **End-User Runtime** | None required for self-contained `CarbonMonitor-Setup-x64.exe` / `publish/` build |
| **Elevation** | Standard user (`asInvoker`) supported; some hardware MSR/NVMe thermal/ETW sensors may require Administrator privileges |

---

## Installation & Uninstallation

1. Run `CarbonMonitor-Setup-x64.exe` (installs per-user by default without requiring admin).
2. Optionally check **Launch Carbon Monitor at Windows sign-in** during setup (unchecked by default).
3. To uninstall, use **Windows Settings -> Apps -> Carbon Monitor -> Uninstall**. App binaries and Carbon Monitor's own `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CarbonMonitor` entry are removed. User settings in `%LocalAppData%\CarbonMonitor\` are retained across upgrades and uninstall unless manually deleted.

---

## For Developers

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS) on Windows 10/11 x64
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for building `CarbonMonitor-Setup-x64.exe`)

### Build, Test, Publish & Package

```powershell
# Restore, build, and run unit tests
dotnet restore CarbonMonitor.sln
dotnet build CarbonMonitor.sln -c Release --no-restore
dotnet test CarbonMonitor.sln -c Release --no-build

# Publish self-contained win-x64 bundle
dotnet publish src/CarbonMonitor.App/CarbonMonitor.App.csproj -c Release -r win-x64 --self-contained true -o publish/

# Build Inno Setup installer
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\CarbonMonitor.iss
```

Or run the automated PowerShell script:

```powershell
.\build.ps1 -Clean -Test -Publish -Installer
```

See [docs/BUILDING.md](docs/BUILDING.md) for full details.

---

## Project Structure

```text
CarbonMonitor/
  CarbonMonitor.sln
  Directory.Build.props
  build.ps1
  LICENSE
  README.md
  src/
    CarbonMonitor.App/                # WinForms GUI, tray, overlay, hotkey, dashboard, settings, diagnostics
    CarbonMonitor.Core/               # Sensor models, provider contracts, MonitoringEngine, AlertEvaluator, AdaptivePollingPolicy
    CarbonMonitor.Infrastructure/     # Rolling FileLogger, SingleInstanceGuard, StartupManager, LocalSettingsService, DiagnosticReportBuilder
    CarbonMonitor.Providers.Windows/  # PDH/WMI/Win32 providers (CPU, Memory, Disk, Network probe, GPU Engine, Power state)
    CarbonMonitor.Providers.Hardware/ # LibreHardwareMonitorLib, Intel iGPU, NVIDIA NVML, AMD ADLX probe, NVMe WMI, ThinkPad probe
    CarbonMonitor.Gaming/             # GameProcessDetector, GameProcessWatcher, FpsCalculator, PresentMon/DXGI ETW provider
  tests/
    CarbonMonitor.Core.Tests/
    CarbonMonitor.Providers.Tests/
  installer/
    CarbonMonitor.iss
  docs/
    BUILDING.md
    THIRD-PARTY-NOTICES.md
    RELEASE-CHECKLIST.md
    HARDWARE-VALIDATION.md
```

---

## Known Limitations

- **Intel UHD 620 (ThinkPad X1 Carbon 6th Gen)**: Integrated GPU does not expose a separate GPU temperature sensor; GPU utilization and shared memory are read from Windows WDDM counters (`Windows.GpuEngine`). CPU package temperature is reported separately under CPU and is never relabeled as GPU temperature.
- **Elevation-Dependent Sensors**: Low-level MSR CPU temperatures/power (`LibreHardwareMonitorLib`), `MSFT_StorageReliabilityCounter` SSD temperatures, and real-time ETW DXGI sessions (`TraceEvent`) may report `PermissionRequired` when run as a non-elevated standard user.
- **NVIDIA NVML & AMD ADLX**: Both provider paths are implemented with capability detection and safe fallbacks (`nvml.dll` is loaded dynamically from the host NVIDIA driver, never bundled), but have **not** been tested on physical NVIDIA or AMD hardware in this repository build.
- **FPS Capture & Anti-Cheat (Valorant / Vanguard)**: Optional external ETW / `PresentMon.exe` frame capture is not guaranteed compatible with Riot Vanguard or other kernel-level anti-cheat systems. If blocked, Carbon Monitor degrades safely without attempting evasion.
- **Exclusive Fullscreen**: Windows topmost external overlays may not appear over exclusive fullscreen D3D/Vulkan games; use Borderless Windowed mode.

---

## License & Notices

Licensed under the **MIT License** ([LICENSE](LICENSE)). See [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md) for third-party dependency licenses and redistribution terms.
