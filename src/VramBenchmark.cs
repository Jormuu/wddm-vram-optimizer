using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRAMOptimizerApp {
    public class VramBenchmark {
        public static volatile bool CancelRequested = false;

        // Keep at least 1.5 GiB of OS commit charge free so benchmark saturation never freezes DWM/Windows
        private const ulong OS_SAFETY_CUSHION_BYTES = 1536UL * 1024UL * 1024UL;

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_BUFFER_DESC {
            public uint ByteWidth;
            public int Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
            public uint StructureByteStride;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_SUBRESOURCE_DATA {
            public IntPtr pSysMem;
            public uint SysMemPitch;
            public uint SysMemSlicePitch;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1 {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_QUERY_VIDEO_MEMORY_INFO {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIFactory1 {
            void _VtblGap1_9();
            [PreserveSig]
            int EnumAdapters1(uint Adapter, out IntPtr ppAdapter);
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter1 {
            void _VtblGap1_7();
            [PreserveSig]
            int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        }

        [ComImport, Guid("645967A4-1392-4310-A798-8053CE3E93FD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter3 {
            void _VtblGap1_11();
            [PreserveSig]
            int QueryVideoMemoryInfo(uint NodeIndex, int MemorySegmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
            [PreserveSig]
            int SetVideoMemoryReservation(uint NodeIndex, int MemorySegmentGroup, ulong Reservation);
        }

        // Single vtable slot 3 (ID3D11Device::CreateBuffer); IntPtr pInitialData supports both pinned data and IntPtr.Zero
        [ComImport, Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11Device {
            [PreserveSig]
            int CreateBuffer(ref D3D11_BUFFER_DESC pDesc, IntPtr pInitialData, out IntPtr ppBuffer);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void FlushDelegate(IntPtr context);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static double GetTotalSystemRamGb() {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref m)) {
                return m.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            }
            return 32.0;
        }

        private static ulong GetAvailableCommitBytes() {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref m)) {
                return m.ullAvailPageFile;
            }
            return ulong.MaxValue;
        }

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out ID3D11Device ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext
        );

        private static void SafeReleaseComObject(object comObj) {
            if (comObj != null && Marshal.IsComObject(comObj)) {
                try {
                    Marshal.FinalReleaseComObject(comObj);
                } catch (Exception ex) {
                    Trace.WriteLine("COM RCW release warning: " + ex.Message);
                }
            }
        }

        private static void SafeReleasePtr(ref IntPtr ptr) {
            if (ptr != IntPtr.Zero) {
                try {
                    Marshal.Release(ptr);
                } catch (Exception ex) {
                    Trace.WriteLine("COM IntPtr release warning: " + ex.Message);
                }
                ptr = IntPtr.Zero;
            }
        }

        public class GpuAdapterInfo {
            public int Index { get; set; }
            public string Name { get; set; }
            public long Luid { get; set; }
            public uint Flags { get; set; }
            public bool IsUma { get; set; }
            public double DedicatedMb { get; set; }
            public double LocalBudgetMb { get; set; }
            public double LocalUsageMb { get; set; }
            public double SharedMaxMb { get; set; }
            public double NonLocalBudgetMb { get; set; }
            public double NonLocalUsageMb { get; set; }

            /// <summary>
            /// Total non-overlapping addressable GPU memory budget (never double-counts UMA shared RAM).
            /// </summary>
            public double TotalPoolBudgetMb {
                get { return IsUma ? LocalBudgetMb : (LocalBudgetMb + NonLocalBudgetMb); }
            }

            public override string ToString() {
                if (IsUma) {
                    return string.Format("{0} (Integrated UMA — {1:F1} GiB Unified Pool)",
                        Name, LocalBudgetMb / 1024.0);
                }
                return string.Format("{0} ({1:F1} GiB Dedicated + {2:F1} GiB Shared)",
                    Name, DedicatedMb / 1024.0, NonLocalBudgetMb / 1024.0);
            }
        }

        public static List<GpuAdapterInfo> GetAllGpuAdapters() {
            List<GpuAdapterInfo> list = new List<GpuAdapterInfo>();
            Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            IDXGIFactory1 factory = null;
            try {
                if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == null) {
                    return list;
                }

                for (uint i = 0; i < 16; i++) {
                    IntPtr pAd = IntPtr.Zero;
                    if (factory.EnumAdapters1(i, out pAd) != 0 || pAd == IntPtr.Zero) break;

                    object rcw = null;
                    try {
                        rcw = Marshal.GetObjectForIUnknown(pAd);
                        IDXGIAdapter1 ad1 = (IDXGIAdapter1)rcw;
                        DXGI_ADAPTER_DESC1 desc;
                        if (ad1.GetDesc1(out desc) != 0) continue;

                        // Skip software emulation adapters (DXGI_ADAPTER_FLAG_SOFTWARE = 2)
                        if ((desc.Flags & 2) != 0) continue;

                        double descDedMb = (ulong)desc.DedicatedVideoMemory / 1048576.0;
                        double descSharedMb = (ulong)desc.SharedSystemMemory / 1048576.0;

                        ulong locBudget = (ulong)desc.DedicatedVideoMemory;
                        ulong locUsage = 0;
                        ulong nonLocBudget = (ulong)desc.SharedSystemMemory;
                        ulong nonLocUsage = 0;
                        bool hasVmi = false;

                        IDXGIAdapter3 a3 = rcw as IDXGIAdapter3;
                        if (a3 != null) {
                            DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nonLocVmi;
                            int hrLoc = a3.QueryVideoMemoryInfo(0, 0, out locVmi);
                            int hrNonLoc = a3.QueryVideoMemoryInfo(0, 1, out nonLocVmi);
                            if (hrLoc == 0 && hrNonLoc == 0) {
                                hasVmi = true;
                                locBudget = locVmi.Budget;
                                locUsage = locVmi.CurrentUsage;
                                nonLocBudget = nonLocVmi.Budget;
                                nonLocUsage = nonLocVmi.CurrentUsage;
                            }
                        }

                        bool isUma = WddmCalculator.IsUnifiedMemoryAdapter(descDedMb, locBudget, nonLocBudget, hasVmi);

                        GpuAdapterInfo info = new GpuAdapterInfo {
                            Index = (int)i,
                            Name = desc.Description,
                            Luid = desc.AdapterLuid,
                            Flags = desc.Flags,
                            IsUma = isUma,
                            DedicatedMb = descDedMb,
                            LocalBudgetMb = locBudget / 1048576.0,
                            LocalUsageMb = locUsage / 1048576.0,
                            SharedMaxMb = descSharedMb,
                            NonLocalBudgetMb = isUma ? 0.0 : (nonLocBudget / 1048576.0),
                            NonLocalUsageMb = isUma ? 0.0 : (nonLocUsage / 1048576.0)
                        };
                        list.Add(info);
                    } finally {
                        SafeReleaseComObject(rcw);
                        SafeReleasePtr(ref pAd);
                    }
                }
            } finally {
                SafeReleaseComObject(factory);
            }
            return list;
        }

        public static void RunFullBenchmark(long targetAdapterLuid, int fallbackAdapterIndex, Action<string> logCallback, Action<int> progressCallback, Action onFinished) {
            CancelRequested = false;
            IDXGIFactory1 factory = null;
            IntPtr targetAdapterPtr = IntPtr.Zero;
            object adapterRcw = null;
            ID3D11Device dev = null;
            IntPtr ctx = IntPtr.Zero;
            GCHandle pinnedBytesHandle = default(GCHandle);
            GCHandle pinnedSrdHandle = default(GCHandle);
            List<IntPtr> buffers = new List<IntPtr>();

            try {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == null) {
                    logCallback("Error: Failed to create DXGI 1.1 Factory.");
                    return;
                }

                string targetDesc = "";
                double descDedicatedMb = 0;
                ulong descDedicatedBytes = 0;
                ulong descSharedBytes = 0;

                // Match adapter by LUID first (stable across enumeration changes), then fallback index, then largest VRAM
                ulong bestFallbackVram = 0;
                for (uint i = 0; i < 16; i++) {
                    IntPtr pAd = IntPtr.Zero;
                    if (factory.EnumAdapters1(i, out pAd) != 0 || pAd == IntPtr.Zero) break;

                    object tmpRcw = null;
                    bool keepPtr = false;
                    try {
                        tmpRcw = Marshal.GetObjectForIUnknown(pAd);
                        IDXGIAdapter1 ad1 = (IDXGIAdapter1)tmpRcw;
                        DXGI_ADAPTER_DESC1 desc;
                        if (ad1.GetDesc1(out desc) != 0 || (desc.Flags & 2) != 0) {
                            continue;
                        }

                        bool exactMatch = (targetAdapterLuid != 0 && desc.AdapterLuid == targetAdapterLuid)
                                       || (targetAdapterLuid == 0 && fallbackAdapterIndex >= 0 && (int)i == fallbackAdapterIndex);
                        if (exactMatch) {
                            SafeReleasePtr(ref targetAdapterPtr);
                            targetAdapterPtr = pAd;
                            keepPtr = true;
                            targetDesc = desc.Description;
                            descDedicatedBytes = (ulong)desc.DedicatedVideoMemory;
                            descDedicatedMb = descDedicatedBytes / 1048576.0;
                            descSharedBytes = (ulong)desc.SharedSystemMemory;
                            break;
                        }

                        ulong vram = (ulong)desc.DedicatedVideoMemory;
                        if (targetAdapterPtr == IntPtr.Zero || vram > bestFallbackVram) {
                            SafeReleasePtr(ref targetAdapterPtr);
                            targetAdapterPtr = pAd;
                            keepPtr = true;
                            bestFallbackVram = vram;
                            targetDesc = desc.Description;
                            descDedicatedBytes = vram;
                            descDedicatedMb = vram / 1048576.0;
                            descSharedBytes = (ulong)desc.SharedSystemMemory;
                        }
                    } finally {
                        SafeReleaseComObject(tmpRcw);
                        if (!keepPtr) {
                            SafeReleasePtr(ref pAd);
                        }
                    }
                }

                if (targetAdapterPtr == IntPtr.Zero) {
                    logCallback("Error: No compatible hardware graphics adapter found.");
                    return;
                }

                adapterRcw = Marshal.GetObjectForIUnknown(targetAdapterPtr);
                IDXGIAdapter3 a3 = adapterRcw as IDXGIAdapter3;

                ulong localBudget = descDedicatedBytes;
                ulong nonLocalBudget = descSharedBytes;
                ulong maxLocalReservation = 0;
                bool hasVmi = false;

                if (a3 != null) {
                    DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nlocVmi;
                    if (a3.QueryVideoMemoryInfo(0, 0, out locVmi) == 0 &&
                        a3.QueryVideoMemoryInfo(0, 1, out nlocVmi) == 0) {
                        hasVmi = true;
                        localBudget = locVmi.Budget;
                        nonLocalBudget = nlocVmi.Budget;
                        maxLocalReservation = locVmi.AvailableForReservation;
                    }
                }

                bool isUma = WddmCalculator.IsUnifiedMemoryAdapter(descDedicatedMb, localBudget, nonLocalBudget, hasVmi);
                if (isUma) {
                    nonLocalBudget = 0;
                }

                ulong totalMaxBytes = localBudget + nonLocalBudget;
                double totalMaxGiB = totalMaxBytes / 1073741824.0;
                double localBudgetGiB = localBudget / 1073741824.0;
                double nonLocalGiB = nonLocalBudget / 1073741824.0;

                logCallback(string.Format("Target GPU Device: {0}{1}", targetDesc, isUma ? " [Integrated UMA]" : " [Discrete PCIe]"));
                if (isUma) {
                    logCallback(string.Format("Unified UMA Budget: {0:F2} GiB ({1:N0} MiB) | BIOS Carve-Out: {2:N0} MiB",
                        localBudgetGiB, localBudget / 1048576, descDedicatedBytes / 1048576));
                } else {
                    logCallback(string.Format("Dedicated LOCAL Budget: {0:F2} GiB ({1:N0} MiB)", localBudgetGiB, localBudget / 1048576));
                    logCallback(string.Format("Shared NON_LOCAL Budget: {0:F2} GiB ({1:N0} MiB)", nonLocalGiB, nonLocalBudget / 1048576));
                    logCallback(string.Format("-> Full Combined Pool: {0:F2} GiB ({1:N0} MiB)", totalMaxGiB, totalMaxBytes / 1048576));
                }
                logCallback("--------------------------------------------------");

                int featLevel;
                int hr = D3D11CreateDevice(targetAdapterPtr, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, out dev, out featLevel, out ctx);
                if (hr != 0 || dev == null) {
                    logCallback(string.Format("Failed to initialize Direct3D 11 device (HRESULT=0x{0:X8})", hr));
                    return;
                }

                uint chunkSize = 64 * 1024 * 1024; // 64 MiB blocks
                byte[] initialBytes = new byte[chunkSize];
                for (int i = 0; i < initialBytes.Length; i += 4096) initialBytes[i] = 0x5A;
                pinnedBytesHandle = GCHandle.Alloc(initialBytes, GCHandleType.Pinned);

                D3D11_SUBRESOURCE_DATA srd = new D3D11_SUBRESOURCE_DATA {
                    pSysMem = pinnedBytesHandle.AddrOfPinnedObject(),
                    SysMemPitch = chunkSize,
                    SysMemSlicePitch = chunkSize
                };
                pinnedSrdHandle = GCHandle.Alloc(srd, GCHandleType.Pinned);
                IntPtr srdPtr = pinnedSrdHandle.AddrOfPinnedObject();

                // Phase 1: D3D11_USAGE_DEFAULT -> DXGI_MEMORY_SEGMENT_GROUP_LOCAL
                D3D11_BUFFER_DESC bdLocal = new D3D11_BUFFER_DESC {
                    ByteWidth = chunkSize,
                    Usage = 0,
                    BindFlags = 0x8, // D3D11_BIND_UNORDERED_ACCESS
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                    StructureByteStride = 0
                };

                // Phase 2 (Discrete GPUs): D3D11_USAGE_STAGING -> DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL (PCIe Shared GPU Memory)
                D3D11_BUFFER_DESC bdShared = new D3D11_BUFFER_DESC {
                    ByteWidth = chunkSize,
                    Usage = 3,
                    BindFlags = 0,
                    CPUAccessFlags = 0x30000, // D3D11_CPU_ACCESS_READ | D3D11_CPU_ACCESS_WRITE
                    MiscFlags = 0,
                    StructureByteStride = 0
                };

                FlushDelegate flush = null;
                if (ctx != IntPtr.Zero) {
                    try {
                        IntPtr vtbl = Marshal.ReadIntPtr(ctx);
                        IntPtr flushPtr = Marshal.ReadIntPtr(vtbl, 111 * IntPtr.Size);
                        flush = (FlushDelegate)Marshal.GetDelegateForFunctionPointer(flushPtr, typeof(FlushDelegate));
                    } catch (Exception ex) {
                        Trace.WriteLine("D3D11 Flush delegate lookup warning: " + ex.Message);
                    }
                }

                ulong totalAllocated = 0;
                ulong phase1Allocated = 0;
                bool forcePhase2 = (!isUma && localBudget == 0);
                bool announcedPhase2 = false;

                logCallback("Allocating GPU-resident Direct3D 11 buffers (64 MiB blocks)...");
                while (totalAllocated + chunkSize <= totalMaxBytes) {
                    if (CancelRequested) {
                        logCallback("-> Allocation halted by user.");
                        break;
                    }

                    bool isPhase1 = isUma || (!forcePhase2 && phase1Allocated + chunkSize <= localBudget);

                    // Guard system commit charge when allocating host-backed memory (UMA or Phase 2 PCIe Staging)
                    if (isUma || !isPhase1) {
                        ulong availCommit = GetAvailableCommitBytes();
                        if (availCommit < chunkSize + OS_SAFETY_CUSHION_BYTES) {
                            logCallback(string.Format(
                                "-> Reached OS commit safety cushion ({0:F2} GiB kept free so Windows desktop stays responsive).",
                                OS_SAFETY_CUSHION_BYTES / 1073741824.0));
                            break;
                        }
                    }

                    if (isPhase1 && a3 != null && maxLocalReservation > 0) {
                        ulong desiredRes = Math.Min(phase1Allocated + chunkSize, maxLocalReservation);
                        a3.SetVideoMemoryReservation(0, 0, desiredRes);
                    }

                    IntPtr buf = IntPtr.Zero;
                    int bhr;
                    if (isPhase1) {
                        bhr = dev.CreateBuffer(ref bdLocal, srdPtr, out buf);
                        if (bhr != 0 || buf == IntPtr.Zero) {
                            bhr = dev.CreateBuffer(ref bdLocal, IntPtr.Zero, out buf);
                        }

                        // If Dedicated LOCAL saturated early (e.g. other apps hold VRAM) on a discrete GPU, advance to Phase 2
                        if ((bhr != 0 || buf == IntPtr.Zero) && !isUma && nonLocalBudget >= chunkSize) {
                            forcePhase2 = true;
                            logCallback(string.Format(
                                ">> [PHASE 1 SATURATED] Dedicated VRAM filled at {0:F2} GiB (accounting for active apps).",
                                phase1Allocated / 1073741824.0));
                            continue;
                        }
                    } else {
                        if (!announcedPhase2) {
                            announcedPhase2 = true;
                            if (flush != null) { try { flush(ctx); } catch { } }
                            logCallback(string.Format(">> [PHASE 1 COMPLETE] Dedicated VRAM ({0:F2} GiB) saturated!", phase1Allocated / 1073741824.0));
                            logCallback(">> [PHASE 2 ACTIVE] Allocating into Shared GPU Memory via PCIe aperture (Reflected in Task Manager)...");
                        }
                        bhr = dev.CreateBuffer(ref bdShared, srdPtr, out buf);
                        if (bhr != 0 || buf == IntPtr.Zero) {
                            bhr = dev.CreateBuffer(ref bdShared, IntPtr.Zero, out buf);
                        }
                    }

                    if (bhr != 0 || buf == IntPtr.Zero) {
                        logCallback(string.Format(
                            "-> Driver reached allocation ceiling at {0:F2} GiB ({1:N0} MiB, HRESULT=0x{2:X8}).",
                            totalAllocated / 1073741824.0, totalAllocated / 1048576, bhr));
                        break;
                    }

                    buffers.Add(buf);
                    totalAllocated += chunkSize;
                    if (isPhase1) phase1Allocated += chunkSize;

                    if (totalAllocated % (1024UL * 1024 * 1024) == 0 || totalAllocated + chunkSize > totalMaxBytes) {
                        if (flush != null) {
                            try { flush(ctx); } catch { }
                        }

                        int pct = (int)((totalAllocated * 100UL) / Math.Max(1UL, totalMaxBytes));
                        if (pct > 100) pct = 100;
                        progressCallback(pct);

                        ulong liveLocalUsage = phase1Allocated;
                        ulong liveNonLocalUsage = totalAllocated - phase1Allocated;
                        if (a3 != null) {
                            DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nlocVmi;
                            if (a3.QueryVideoMemoryInfo(0, 0, out locVmi) == 0 &&
                                a3.QueryVideoMemoryInfo(0, 1, out nlocVmi) == 0) {
                                liveLocalUsage = locVmi.CurrentUsage;
                                liveNonLocalUsage = isUma ? 0UL : nlocVmi.CurrentUsage;
                            }
                        }

                        if (isUma) {
                            double umaGiB = liveLocalUsage / 1073741824.0;
                            double umaPct = localBudget > 0 ? (liveLocalUsage * 100.0 / localBudget) : 0;
                            logCallback(string.Format(
                                "  -> [UMA UNIFIED POOL] Allocated: {0:F2} / {1:F2} GiB ({2}%) | Driver Resident: {3:F2} GiB ({4:F0}%)",
                                totalAllocated / 1073741824.0, totalMaxGiB, pct, umaGiB, umaPct));
                        } else {
                            double dedGiB = liveLocalUsage / 1073741824.0;
                            double shGiB = liveNonLocalUsage / 1073741824.0;
                            double dedPct = localBudget > 0 ? (liveLocalUsage * 100.0 / localBudget) : 0;
                            double shPct = nonLocalBudget > 0 ? (liveNonLocalUsage * 100.0 / nonLocalBudget) : 0;
                            string phaseTag = isPhase1 ? "[PHASE 1 - DEDICATED]" : "[PHASE 2 - SHARED PCIe]";
                            logCallback(string.Format(
                                "  -> {0} Allocated: {1:F2} / {2:F2} GiB ({3}%) | DXGI Local: {4:F2} GiB ({5:F0}%) | DXGI Shared: {6:F2} GiB ({7:F0}%)",
                                phaseTag, totalAllocated / 1073741824.0, totalMaxGiB, pct, dedGiB, dedPct, shGiB, shPct));
                        }
                    }
                }

                if (!CancelRequested) {
                    progressCallback(100);
                }

                logCallback("--------------------------------------------------");
                logCallback(string.Format("★ BENCHMARK SUMMARY: Allocated {0:F2} GiB ({1:N0} MiB) across {2} Direct3D 11 buffers.",
                    totalAllocated / 1073741824.0, totalAllocated / 1048576, buffers.Count));
                logCallback("--------------------------------------------------");
            } catch (Exception ex) {
                logCallback("Benchmark encountered an unexpected error: " + ex.Message);
            } finally {
                logCallback("Releasing all allocated GPU & staging buffers...");
                if (pinnedSrdHandle.IsAllocated) {
                    try { pinnedSrdHandle.Free(); } catch { }
                }
                if (pinnedBytesHandle.IsAllocated) {
                    try { pinnedBytesHandle.Free(); } catch { }
                }
                for (int i = 0; i < buffers.Count; i++) {
                    IntPtr b = buffers[i];
                    SafeReleasePtr(ref b);
                }
                buffers.Clear();

                SafeReleasePtr(ref ctx);
                SafeReleaseComObject(dev);
                dev = null;

                if (adapterRcw is IDXGIAdapter3) {
                    try { ((IDXGIAdapter3)adapterRcw).SetVideoMemoryReservation(0, 0, 0); } catch { }
                }
                SafeReleaseComObject(adapterRcw);
                adapterRcw = null;

                SafeReleasePtr(ref targetAdapterPtr);
                SafeReleaseComObject(factory);
                factory = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();

                logCallback("✔ All Direct3D 11 buffers & COM interfaces released cleanly.");
                onFinished();
            }
        }
    }
}
