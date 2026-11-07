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
**Tested on:** Windows 11 Pro (Build 26300) | ASUS TUF Gaming A15 | Ryzen 7 7735HS | RTX 4050 Laptop GPU | 32 GB RAM
---

## Key Highlights

* **Dynamic Bounds:** Sliders automatically scale their range and stock ceilings based on your detected GPU VRAM (`vramMb`) and physical system RAM (`totalRamGb`).
* **Real-time Live Badges:** Dragging sliders dynamically calculates and displays:
  * **Fragmentation Buffer Slider:** Shows current reserve and live unlocked VRAM savings (`+384 MiB Unlocked`).
  * **Process Budget Cap Slider:** Shows foreground app throttle cap ) and live unlocked VRAM (`+224 MiB Unlocked`).
  * **Shared System RAM Slider:** Shows exact percentage (`50%` to `90%`) and computes the exact physical memory cap in gigabytes based on your installed RAM.
* **Hardware-Accelerated GPU Scheduling (HAGS):** Clean toggle switch (`Disabled` / `Enabled`).

---

## 📄 License & Attribution

This project is licensed under the **[MIT License](LICENSE)**.

* **You are free to:**
  * View, copy, fork, modify, merge, publish, distribute, sublicense, and use the source code for any personal or commercial project.
* **Condition (Attribution Required):**
  * You **must include** the original copyright notice and credit to **HASSAN ALHRBI ([@Jormuu](https://github.com/Jormuu))** in all copies or substantial portions of the software.
