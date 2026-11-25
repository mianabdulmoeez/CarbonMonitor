# Third-Party Notices

Carbon Monitor incorporates or dynamically interacts with the following third-party libraries and tools. Every dependency's license and redistribution terms are documented below.

---

## 1. LibreHardwareMonitorLib

- **NuGet Package:** `LibreHardwareMonitorLib` (Version `0.9.6`)
- **License:** Mozilla Public License 2.0 (`MPL-2.0`)
- **Source:** https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- **Usage:** Read-only access to CPU, GPU, storage, and motherboard temperature/fan/power sensors (`IsControllerEnabled = false`).
- **Redistribution Terms:** Under MPL-2.0, `LibreHardwareMonitorLib` may be bundled in binary form alongside MIT-licensed application code. Any modifications to MPL-2.0 source files must remain licensed under MPL-2.0 and their source made available. Carbon Monitor uses the unmodified NuGet binary. Full text: https://www.mozilla.org/en-US/MPL/2.0/

---

## 2. Microsoft.Diagnostics.Tracing.TraceEvent

- **NuGet Package:** `Microsoft.Diagnostics.Tracing.TraceEvent` (Version `4.0.2`)
- **License:** MIT License
- **Source:** https://github.com/microsoft/perfview
- **Usage:** Optional Windows ETW consumer for `Microsoft-Windows-DXGI` Present timestamps in `CarbonMonitor.Gaming`.

---

## 3. System.Diagnostics.PerformanceCounter & System.Management

- **NuGet Packages:** `System.Diagnostics.PerformanceCounter` (`10.0.0`), `System.Management` (`10.0.0`)
- **License:** MIT License
- **Source:** https://github.com/dotnet/runtime
- **Usage:** Windows PDH counters (`Processor Information`, `PhysicalDisk`, `GPU Engine`, `GPU Adapter Memory`) and WMI hardware discovery (`Win32_Processor`, `Win32_VideoController`, `MSFT_PhysicalDisk`).

---

## 4. .NET 10 Runtime (Self-Contained Deployment)

- **License:** MIT License
- **Source:** https://github.com/dotnet/runtime
- **Usage:** Redistributed in the self-contained `win-x64` publish directory and installer so end users do not need to install .NET separately.

---

## 5. NVIDIA NVML (`nvml.dll`) — Host-Provided, Not Bundled

- **License:** Proprietary — NVIDIA End User License Agreement
- **Source:** Installed by the user's NVIDIA Display Driver (`System32\nvml.dll` or `%ProgramFiles%\NVIDIA Corporation\NVSMI\nvml.dll`)
- **Redistribution Compliance:** Carbon Monitor **does not bundle or redistribute** `nvml.dll` or any proprietary NVIDIA binary. It loads `nvml.dll` dynamically via `NativeLibrary.TryLoad` only if an NVIDIA driver is installed on the host machine.

---

## 6. AMD ADLX (`amdadlx64.dll` / `atiadlxx.dll`) — Host-Provided, Not Bundled

- **License:** Proprietary — AMD Software License Agreement
- **Source:** Installed by the user's AMD Radeon Software driver / https://github.com/GPUOpen-LibrariesAndSDKs/ADLX
- **Redistribution Compliance:** Carbon Monitor **does not bundle or redistribute** AMD proprietary binaries. It probes host driver DLL availability and falls back to `Windows.GpuEngine` and `LibreHardwareMonitorLib`.

---

## 7. Intel PresentMon CLI (Optional External Binary)

- **License:** MIT License
- **Source:** https://github.com/GameTechDev/PresentMon
- **Usage:** Optional external command-line telemetry mode (`PresentMon.exe --process_id <pid> --output_stdout`) if supplied alongside the application.

---

## 8. Test & Build Tooling (Not Distributed to End Users)

- `xUnit` (Apache-2.0), `xunit.runner.visualstudio` (Apache-2.0), `Microsoft.NET.Test.Sdk` (MIT), `Moq` (BSD-3-Clause) — used only in test projects.
- **Inno Setup 6** (Inno Setup License) — build-time installer compiler (`ISCC.exe`) producing `CarbonMonitor-Setup-x64.exe`.
