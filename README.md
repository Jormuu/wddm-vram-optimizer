# Windows WDDM & VRAM Optimizer (`VRAMOptimizer.exe`)

[![Build & Test](https://github.com/Jormuu/wddm-vram-optimizer/actions/workflows/build.yml/badge.svg)](https://github.com/Jormuu/wddm-vram-optimizer/actions/workflows/build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/Jormuu/wddm-vram-optimizer?color=38BDF8&label=Latest%20Release)](https://github.com/Jormuu/wddm-vram-optimizer/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10 / 11 x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://github.com/Jormuu/wddm-vram-optimizer)

A standalone, zero-dependency 64-bit Windows utility to configure native **WDDM (`dxgmms2.sys`)** memory management reserves, scale shared PCIe / UMA system RAM limits, and benchmark real allocatable GPU memory via Direct3D 11 and DXGI 1.4.

100% vendor-neutral (**NVIDIA GeForce**, **AMD Radeon**, **Intel Arc / Iris**) — preserves Secure Boot, kernel anti-cheat, and laptop S0 Modern Standby. See **[`docs/WDDM_Internals.md`](docs/WDDM_Internals.md)** for the full `dxgmms2.sys` disassembly and budget formulas.

> [!WARNING]
> **Disclaimer:** This software is **fully AI-coded** and modifies undocumented Windows graphics memory manager (`dxgmms2.sys`) registry values. It is provided **"as is" without warranty of any kind — use it at your own risk.**

---

## 📥 Download Executable

👉 **[Download `VRAMOptimizer.exe` (v1.2.0)](https://github.com/Jormuu/wddm-vram-optimizer/releases/download/v1.2.0/VRAMOptimizer.exe)**

*(Includes UAC administrator manifest and `PerMonitorV2` High-DPI support. Unsigned binary — if Windows SmartScreen prompts, click "More info → Run anyway", or build from source below in 1 second.)*

### Build & Test from Source

Uses the C# compiler built into Windows' .NET Framework 4.8 (nothing external to install):

```bat
build.cmd
test.cmd
```

`Launch_VRAM_Optimizer.bat` also compiles `VRAMOptimizer.exe` automatically if it is missing.

---

**Tested on:** Windows 11 Pro (Build 26300) | ASUS TUF Gaming A15 | Ryzen 7 7735HS | RTX 4050 Laptop GPU + Radeon 680M | 32 GB RAM

| Profile (`RTX 4050 6 GB`) | WDDM Reserve | Usable DXGI `LOCAL` Budget | Shared System RAM (`32 GB` Host) |
| :--- | :---: | :---: | :---: |
| **Stock Windows 11** | `768 MiB` (`512 + 256`) | `5,152 MiB` (`5.03 GiB`) | `15.62 GiB` (`50%`) |
| **★ Recommended** | `160 MiB` (`128 + 32`) | **`5,760 MiB` (`+608 MiB`)** | **`23.43 GiB` (`75%`)** |
| **⚡ Aggressive** | `64 MiB` (`32 + 32`) | **`5,856 MiB` (`+704 MiB`)** | **`28.12 GiB` (`90%`)** |

---

## Key Highlights

* **Exact `dxgmms2.sys` Kernel Cap Sliders:**
  * **Fragmentation Buffer Cap (`32–512 MiB`):** Tunes `MaxVideoMemoryFragmentationBuffer` with real-time per-GPU unlocked VRAM calculation.
  * **Process Budget Cap (`32–256 MiB`):** Tunes `MaxProcessBudgetCapBuffer` with independent registry override handling.
  * **Shared System RAM Limit (`50%–90%`):** Scales `SystemPartitionCommitLimitPercentage` dynamically against installed physical RAM.
  * **Hardware-Accelerated GPU Scheduling (HAGS):** Clean toggle (`HwSchMode = 1` vs `2`).
* **Multi-GPU & Integrated UMA Detection:** Distinguishes discrete PCIe GPUs (`LOCAL` GDDR + `NON_LOCAL` shared) from integrated UMA GPUs (unified DDR pool) with live `F5` hardware refresh.
* **Live Direct3D 11 Benchmark:** Allocates 64 MiB resident GPU & PCIe staging buffers with live `QueryVideoMemoryInfo` telemetry and a `1.5 GiB` OS safety cushion.
* **Validated `.reg` Backup & Restore:** Automatic first-launch snapshot plus one-click `.reg` export and validated import.

---

## 📄 License & Attribution

This project is licensed under the **[MIT License](LICENSE)**.

* **You are free to:** View, copy, fork, modify, merge, publish, distribute, sublicense, and use the source code for any personal or commercial project.
* **Condition (Attribution Required):** You **must include** the original copyright notice and credit to **HASSAN ALHRBI ([@Jormuu](https://github.com/Jormuu))** in all copies or substantial portions of the software.
