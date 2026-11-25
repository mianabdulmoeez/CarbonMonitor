# Building Carbon Monitor

This guide covers building Carbon Monitor from source, running automated tests, publishing a self-contained Windows x64 build, and creating the Inno Setup installer.

---

## Prerequisites

| Tool | Version | Required For |
|---|---|---|
| **.NET 10 SDK (LTS)** | 10.0.x | Restore, build, test, and self-contained publish |
| **Windows 10 / 11 x64** | 10 (1709+) / 11 | WinForms, WMI, PDH counters, ETW, and Inno Setup |
| **Inno Setup 6** | 6.x | Packaging `CarbonMonitor-Setup-x64.exe` |

---

## Command-Line Build & Test

Run from the `CarbonMonitor/` directory:

```powershell
# 1. Restore NuGet packages
dotnet restore CarbonMonitor.sln

# 2. Build solution in Release configuration
dotnet build CarbonMonitor.sln --configuration Release --no-restore

# 3. Run automated unit tests
dotnet test CarbonMonitor.sln --configuration Release --no-build --verbosity normal

# 4. Publish self-contained Windows x64 application (no .NET runtime required on target PC)
dotnet publish src/CarbonMonitor.App/CarbonMonitor.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish/

# 5. Copy license and third-party notices into publish folder (handled automatically by build.ps1)
Copy-Item LICENSE, docs\THIRD-PARTY-NOTICES.md publish\ -Force

# 6. Build the Inno Setup installer (produces output\CarbonMonitor-Setup-x64.exe)
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\CarbonMonitor.iss
```

## Automated Build Script

Use `build.ps1`:

```powershell
# Full pipeline (clean, restore, build, test, publish, installer)
.\build.ps1 -Clean -Test -Publish -Installer

# Build and run unit tests only
.\build.ps1 -Test

# Build and self-contained publish only
.\build.ps1 -Publish
```

## Opening in Visual Studio or VS Code

- **Visual Studio 2022 / 2026**: Open `CarbonMonitor.sln`, set `CarbonMonitor.App` as startup project, and build/debug (`x64` or `Any CPU`).
- **VS Code**: Open the `CarbonMonitor/` folder with the C# Dev Kit extension installed and run `dotnet build` / `dotnet test` from the integrated terminal.

## Optional External PresentMon CLI

If you wish to pair Carbon Monitor's `Gaming.PresentMon` provider with Intel's standalone `PresentMon.exe` CLI (MIT License) rather than in-process `TraceEvent` DXGI ETW, place `PresentMon.exe` in the application directory or `tools\PresentMon.exe` before enabling FPS capture in **Settings -> Game & Network**.
