# Windows WDDM & VRAM Optimizer (`VRAMOptimizer.exe`)

[![GitHub Release](https://img.shields.io/github/v/release/Jormuu/wddm-vram-optimizer?color=38BDF8&label=Latest%20Release)](https://github.com/Jormuu/wddm-vram-optimizer/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10 / 11](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](https://github.com/Jormuu/wddm-vram-optimizer)

A standalone, single-file, zero-dependency native Windows 64-bit executable (`.exe`) to configure reverse-engineered **WDDM (`dxgmms2.sys`)** memory management settings, monitor live VRAM budgets, and benchmark real allocatable video memory and shared system RAM over PCIe.

100% vendor-neutral: works identically across **NVIDIA GeForce**, **AMD Radeon**, and **Intel Arc** GPUs.

---

## 📥 Download Executable

Get the latest pre-compiled binary:
👉 **[Download `VRAMOptimizer.exe` (v1.1.0)](https://github.com/Jormuu/wddm-vram-optimizer/releases/download/v1.1.0/VRAMOptimizer.exe)**

*(Embedded UAC administrator manifest prompts for elevation automatically. Zero installation required).*

### Build from Source

`Program.cs` is the single source of truth. To compile it yourself (uses the native C# compiler built into Windows' .NET Framework 4.8, so nothing external to install):

```bat
build.cmd
```

`Launch_VRAM_Optimizer.bat` also compiles the `.exe` automatically if it is missing.

---

## Key Highlights

### 1. Dynamic Fine-Grained Sliders (GPU VRAM Tailored)
* **Dynamic Bounds:** Sliders automatically scale their range and stock ceilings based on your detected GPU VRAM (`vramMb`) and physical system RAM (`totalRamGb`).
* **Real-time Live Badges:** Dragging sliders dynamically calculates and displays:
  * **Fragmentation Buffer Slider:** Shows current reserve (`32 MiB` to `512+ MiB`) and live unlocked VRAM savings (`+384 MiB Unlocked`).
  * **Process Budget Cap Slider:** Shows foreground app throttle cap (`32 MiB` to `256+ MiB`) and live unlocked VRAM (`+224 MiB Unlocked`).
  * **Shared System RAM Slider:** Shows exact percentage (`50%` to `90%`) and computes the exact physical memory cap in gigabytes based on your installed RAM (`~24.0 GB on 32GB RAM`).
* **Hardware-Accelerated GPU Scheduling (HAGS):** Clean toggle switch (`Disabled` / `Enabled`).

---

### 2. Multi-GPU Adapter Selector & Dynamic Re-calibration
* **Interactive GPU Selector (Card 4):** Switch between any detected hardware adapter (e.g., integrated AMD Radeon™ Graphics or Intel® Iris Xe, and discrete NVIDIA GeForce RTX™).
* **Live Re-calibration:** Switching the active GPU instantly recalibrates the dashboard cards, dedicated budgets, and dynamic slider ranges to that specific GPU's hardware limits.

---

### 3. Automatic Two-Phase Memory Benchmark with Live Driver Telemetry
* **Zero Manual Sizing Required:** Automatically queries the selected GPU adapter's physical bounds (Dedicated VRAM + Shared System RAM aperture limit).
* **Two-Phase Hardware Allocation:**
  * **Phase 1 (Dedicated GDDR6 Saturation):** Allocates `D3D11_USAGE_DEFAULT` resident GPU blocks to saturate physical onboard VRAM up to 100% capacity.
  * **Phase 2 (True PCIe Shared GPU RAM Allocation):** Seamlessly shifts to `D3D11_USAGE_STAGING` buffers pinned in the GPU's PCIe aperture (`DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL`). Windows Task Manager actively tracks this memory directly on the GPU's "Shared GPU memory" performance graph!
* **Genuine Hardware Driver Telemetry:** Queries real-time hardware residency metrics directly from `QueryVideoMemoryInfo` during execution.
* **Resilient COM Lifecycle:** Protected by strict `try/finally` blocks ensuring zero COM buffer leaks, memory reclamation upon halting, and clean UI state recovery even if aborted.
* **Instant Stop (`■ Stop` or `Esc`):** Allows stopping the benchmark at any time, immediately releasing all allocated memory.

---

### 4. Safe Registry Backup & 1-Click Restore
* **Automatic Initial Snapshot:** On first launch, the optimizer automatically exports an untampered initial state snapshot to `Backups/Initial_Registry_Backup.reg`.
* **Export `.reg` Backup:** One-click button (`💾 Export .reg Backup`) to export your current configuration into standard Windows Registry format (`.reg`).
* **Restore from `.reg`:** One-click button (`📂 Restore from .reg`) to load and apply any saved backup file with instant GUI refresh.

---

### 5. High-DPI & Modern Desktop Ergonomics
* **PerMonitorV2 High-DPI Manifest:** Crisp, native rendering without font blur on 4K, 1440p, and high-DPI laptop displays.
* **Windows 10 & 11 Explicit Compatibility:** Native OS identity manifest prevents compatibility throttling.
* **Dark Mode & Light Mode:** Toggleable theme with custom-styled, high-contrast controls.
* **Keyboard Shortcuts:**
  * <kbd>F5</kbd> — Refresh live hardware and registry state.
  * <kbd>Ctrl</kbd> + <kbd>S</kbd> — Apply current configuration.
  * <kbd>Esc</kbd> — Halt active memory benchmark.

---

## One-Click Presets

| Preset | Frag Buffer | Process Cap | Shared RAM | Dedicated VRAM Gain | Best For |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **★ Recommended** | `128 MiB` | `32 MiB` | `75%` (`~24 GB`) | **`+608 MiB`** | Gaming + Local AI Workloads |
| **⚡ Aggressive** | `32 MiB` | `32 MiB` | `80%` (`~25.6 GB`) | **`+704 MiB`** | Maximum Local LLM inference headroom |
| **↺ Stock Defaults** | Stock Ceiling | Stock Ceiling | `50%` (`~16 GB`) | Baseline (`0 MiB`) | Reverting to stock Windows settings |

---

## 📄 License & Attribution

This project is licensed under the **[MIT License](LICENSE)**.

* **You are free to:**
  * View, copy, fork, modify, merge, publish, distribute, sublicense, and use the source code for any personal or commercial project.
* **Condition (Attribution Required):**
  * You **must include** the original copyright notice and credit to **HASSAN ALHRBI ([@Jormuu](https://github.com/Jormuu))** in all copies or substantial portions of the software.
