# Release Checklist

Use this checklist before every release of Carbon Monitor. Each item must be verified and checked off before publishing a new version.

**Release Version:** _______________
**Date:** _______________
**Tested By:** _______________

---

## Build & Packaging

- [ ] All tests pass: `dotnet test --configuration Release`
- [ ] Build succeeds: `dotnet build --configuration Release`
- [ ] Publish succeeds: `dotnet publish src/CarbonMonitor.App/CarbonMonitor.App.csproj -c Release -r win-x64 --self-contained true -o publish/`
- [ ] Installer builds with Inno Setup: `ISCC.exe installer/CarbonMonitor.iss`
- [ ] Installer output exists at `output/CarbonMonitor-Setup-x64.exe`
- [ ] Third-party notices included in publish output

## Clean Install Testing

- [ ] Install on clean Windows 10 x64 VM — installs without errors
- [ ] Install on clean Windows 11 x64 VM — installs without errors
- [ ] No .NET runtime pre-installed on test VMs (self-contained validation)
- [ ] No admin elevation required for default installation

## Core Functionality

- [ ] Tray icon appears in the system tray after launch
- [ ] Single instance enforcement works — second launch activates existing window, does not start a duplicate
- [ ] Dashboard opens from tray icon and shows live hardware readings
- [ ] Dashboard updates at the configured polling interval

## Overlay

- [ ] **Alt+R** toggles overlay visibility (default hotkey)
- [ ] Custom hotkey can be set in settings and saved
- [ ] Custom hotkey works after restart
- [ ] Hotkey conflict detection works — warns when a hotkey is already registered by another app
- [ ] Overlay displays correct metrics (CPU temp, GPU temp, utilization, FPS if enabled)
- [ ] Overlay position can be dragged and persists after restart
- [ ] Overlay is visible in borderless windowed games

## Settings & Persistence

- [ ] Settings persist after application restart
- [ ] Startup enable/disable works — toggling "Launch at sign-in" adds/removes the registry entry
- [ ] Startup entry launches the application after Windows reboot
- [ ] Monitoring modes switch correctly (e.g., changing polling interval, enabling/disabling providers)
- [ ] Metric toggles work — enabling/disabling individual metrics updates overlay and dashboard

## Hardware Diagnostics

- [ ] Hardware diagnostics page opens and shows all detected sensors
- [ ] Sensor tree displays CPU, GPU, storage, and other detected hardware
- [ ] Diagnostic report export works — saves a text/JSON report to a user-chosen location
- [ ] Report contains all detected hardware, sensor readings, and provider status

## Multi-Monitor & Display

- [ ] Multi-monitor: overlay can be moved to and displays on each connected monitor
- [ ] Multi-monitor: selected display is saved and restored after restart
- [ ] DPI scaling at 100%: overlay and dashboard UI are readable and correctly sized
- [ ] DPI scaling at 150%: overlay and dashboard UI are readable and correctly sized
- [ ] DPI scaling at 200%: overlay and dashboard UI are readable and correctly sized

## Hardware-Specific Testing

- [ ] **Intel UHD 620** (reference device): utilization via performance counters, no GPU temp (documented)
- [ ] **NVIDIA GPU** test (if available): temperature, utilization, clocks, VRAM, fan speed via NVML
- [ ] **AMD GPU** test (if available): temperature, utilization, clocks, VRAM, fan speed via ADLX/LHM

## Performance

- [ ] CPU usage measured while idle (overlay hidden): target < 1%
- [ ] CPU usage measured while active (overlay visible, 1s polling): target < 2%
- [ ] Working set (RAM) measured while idle: target < 60 MB
- [ ] Working set (RAM) measured while active: target < 80 MB
- [ ] No memory leaks observed over 1-hour continuous run

## Uninstall & Upgrade

- [ ] Uninstaller removes all application files from the install directory
- [ ] Uninstaller removes the startup registry entry
- [ ] Uninstaller removes Start Menu and Desktop shortcuts
- [ ] Settings in `%LocalAppData%\CarbonMonitor\` are **retained** after uninstall (by design)
- [ ] Upgrade install over existing version: settings retained, app functions correctly
- [ ] No orphaned processes after uninstall

## Security & Privacy

- [ ] No admin required for basic operation (overlay, dashboard, most sensors)
- [ ] No network/cloud/telemetry traffic (verify with Wireshark or netstat)
- [ ] No data written outside `%LocalAppData%\CarbonMonitor\` and the install directory

## Anti-Cheat Compatibility

- [ ] Test overlay with Valorant (Vanguard anti-cheat) — document results:
  - Overlay visible? _______________
  - FPS capture works? _______________
  - Game stability affected? _______________
  - Notes: _______________
- [ ] Test overlay with other anti-cheat games if possible — document results

## Final Checks

- [ ] Version number updated in all locations (`.csproj`, installer script, About dialog)
- [ ] THIRD-PARTY-NOTICES.md is up to date with current dependency versions
- [ ] README.md reflects any new features or changed behavior
- [ ] CHANGELOG or release notes drafted
- [ ] Git tag created for the release version
- [ ] Installer signed (if code signing certificate is available)

---

## Sign-Off

| Role | Name | Date | Approved |
|------|------|------|----------|
| Developer | | | ☐ |
| Tester | | | ☐ |

---

*Template version: 1.0 — Update this checklist as new features are added.*
