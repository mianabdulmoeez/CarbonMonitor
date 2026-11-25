# Hardware Validation Report

This document records hardware-specific testing results for Carbon Monitor across different device configurations. Each section represents a test device — copy the template and fill in results for each machine tested.

---

## How to Use This Template

1. Copy the **Device Template** section below for each test machine.
2. Fill in all hardware details and test results.
3. Note any anomalies, missing sensors, or unexpected behavior.
4. Attach or link diagnostic report exports where available.

---

## Device Template

### Device: [Name / Identifier]

**Date Tested:** _______________
**Carbon Monitor Version:** _______________
**Tester:** _______________

#### System Information

| Component | Details |
|-----------|---------|
| **OS** | Windows __ (Build ______) |
| **CPU** | |
| **GPU (Discrete)** | |
| **GPU (Integrated)** | |
| **RAM** | |
| **Storage** | |
| **Display(s)** | |
| **DPI Scaling** | |

#### Driver Versions

| Driver | Version |
|--------|---------|
| **GPU Driver** | |
| **Chipset** | |
| **Other** | |

#### Sensor Detection Results

| Sensor | Detected | Provider | Reading | Notes |
|--------|----------|----------|---------|-------|
| CPU Temperature | ☐ Yes ☐ No | | | |
| CPU Utilization | ☐ Yes ☐ No | | | |
| CPU Clock Speed | ☐ Yes ☐ No | | | |
| CPU Power | ☐ Yes ☐ No | | | |
| GPU Temperature | ☐ Yes ☐ No | | | |
| GPU Utilization | ☐ Yes ☐ No | | | |
| GPU Clock Speed | ☐ Yes ☐ No | | | |
| GPU VRAM Usage | ☐ Yes ☐ No | | | |
| GPU Fan Speed | ☐ Yes ☐ No | | | |
| GPU Power | ☐ Yes ☐ No | | | |
| RAM Usage | ☐ Yes ☐ No | | | |
| Storage Temp | ☐ Yes ☐ No | | | |
| Storage Health | ☐ Yes ☐ No | | | |
| FPS Capture | ☐ Yes ☐ No | | | |

#### Provider Status

| Provider | Status | Notes |
|----------|--------|-------|
| LibreHardwareMonitor | ☐ Active ☐ Failed ☐ N/A | |
| NVIDIA NVML | ☐ Active ☐ Failed ☐ N/A | |
| AMD ADLX | ☐ Active ☐ Failed ☐ N/A | |
| Performance Counters | ☐ Active ☐ Failed ☐ N/A | |
| PresentMon (FPS) | ☐ Active ☐ Failed ☐ N/A | |

#### Overlay Testing

| Test | Result | Notes |
|------|--------|-------|
| Overlay displays correctly | ☐ Pass ☐ Fail | |
| Overlay readable at current DPI | ☐ Pass ☐ Fail | |
| Overlay visible in windowed game | ☐ Pass ☐ Fail | |
| Overlay visible in borderless | ☐ Pass ☐ Fail | |
| Overlay visible in exclusive FS | ☐ Pass ☐ Fail | |
| Hotkey toggles overlay | ☐ Pass ☐ Fail | |
| Position saves/restores | ☐ Pass ☐ Fail | |

#### Performance Metrics

| Metric | Idle (Overlay Hidden) | Active (Overlay Visible) |
|--------|----------------------|--------------------------|
| CPU Usage | | |
| Working Set (MB) | | |
| Handle Count | | |

#### Diagnostic Report

- [ ] Diagnostic report exported successfully
- [ ] Report file: _______________

#### Issues Found

| # | Description | Severity | Workaround |
|---|-------------|----------|------------|
| 1 | | | |
| 2 | | | |
| 3 | | | |

#### Notes

_Additional observations, anomalies, or comments:_

---

## Reference Device: Intel UHD 620 Laptop

### Device: Reference Laptop (Intel UHD 620)

**Date Tested:** _______________
**Carbon Monitor Version:** _______________
**Tester:** _______________

#### System Information

| Component | Details |
|-----------|---------|
| **OS** | |
| **CPU** | Intel Core i5/i7 (8th Gen) |
| **GPU (Discrete)** | None |
| **GPU (Integrated)** | Intel UHD Graphics 620 |
| **RAM** | |
| **Storage** | |

#### Known Expectations for This Device

- **GPU Temperature:** Not available — Intel UHD 620 does not expose a temperature sensor through WMI, LibreHardwareMonitor, or any known API. This is a hardware/driver limitation, not a Carbon Monitor bug.
- **GPU Utilization:** Available via Windows Performance Counters (`GPU Engine` category). Less granular than vendor-specific APIs but functional.
- **GPU Clock Speed:** May be available through LibreHardwareMonitor depending on driver version.
- **FPS Capture:** Should work via PresentMon ETW for most games.

#### Validation Checklist for This Device

- [ ] Application starts without errors
- [ ] No crash or exception related to missing GPU temperature sensor
- [ ] GPU utilization is displayed (via performance counters)
- [ ] Dashboard shows "N/A" or "—" for GPU temperature (not an error)
- [ ] Diagnostic report correctly notes the absence of the GPU temperature sensor
- [ ] All other sensors (CPU, RAM, storage) function normally

---

## NVIDIA GPU Test Device

### Device: [NVIDIA GPU Machine Name]

**Date Tested:** _______________
**Carbon Monitor Version:** _______________
**Tester:** _______________

_(Use the Device Template above and fill in all sections)_

#### NVIDIA-Specific Checks

- [ ] NVML provider loads successfully
- [ ] GPU temperature reads correctly (compare with NVIDIA GeForce Experience or GPU-Z)
- [ ] GPU utilization reads correctly
- [ ] GPU clock speeds read correctly (core and memory)
- [ ] VRAM usage reads correctly
- [ ] Fan speed reads correctly (if applicable — laptops may not expose this)
- [ ] Power consumption reads correctly
- [ ] Multiple NVIDIA GPUs detected (if present, e.g., SLI/NVLink)
- [ ] NVML graceful fallback: uninstall NVIDIA drivers → Carbon Monitor starts without crash, GPU sensors show as unavailable

---

## AMD GPU Test Device

### Device: [AMD GPU Machine Name]

**Date Tested:** _______________
**Carbon Monitor Version:** _______________
**Tester:** _______________

_(Use the Device Template above and fill in all sections)_

#### AMD-Specific Checks

- [ ] ADLX provider loads successfully (or falls back to LibreHardwareMonitor)
- [ ] GPU temperature reads correctly (compare with AMD Adrenalin or GPU-Z)
- [ ] GPU utilization reads correctly
- [ ] GPU clock speeds read correctly (core and memory)
- [ ] VRAM usage reads correctly
- [ ] Fan speed reads correctly
- [ ] Power consumption reads correctly
- [ ] ADLX graceful fallback: if ADLX unavailable → LibreHardwareMonitor provides AMD GPU sensors

> [!NOTE]
> The AMD ADLX provider is implemented but has not been validated on AMD hardware. Initial testing on an AMD system should include detailed comparison against AMD Adrenalin software readings and GPU-Z as reference. Please report any discrepancies.

---

*Add additional device sections as needed. This document serves as the living hardware compatibility record for Carbon Monitor.*
