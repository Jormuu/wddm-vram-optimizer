using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;

namespace VRAMOptimizerApp {
    public class Program {
        [STAThread]
        public static void Main() {
            try {
                bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                if (!isAdmin) {
                    try {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                        psi.UseShellExecute = true;
                        psi.Verb = "runas";
                        Process.Start(psi);
                    } catch {
                        MessageBox.Show("Administrator privileges are required to configure graphics driver memory settings.",
                            "Elevation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                App app = new App();
                VRAMOptimizerWindow appWindow = new VRAMOptimizerWindow();
                app.Run(appWindow.Window);
            } catch (Exception ex) {
                MessageBox.Show("Fatal Error on Startup:\n\n" + ex.ToString(), "VRAM Optimizer Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class App : Application { }

    #region DirectX / Benchmark Structs & Imports
    public class VramBenchmark {
        public static volatile bool CancelRequested = false;

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

        [ComImport, Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11Device {
            [PreserveSig]
            int CreateBuffer(ref D3D11_BUFFER_DESC pDesc, ref D3D11_SUBRESOURCE_DATA pInitialData, out IntPtr ppBuffer);
            [PreserveSig]
            int CreateBufferNull(ref D3D11_BUFFER_DESC pDesc, IntPtr pInitialData, out IntPtr ppBuffer);
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

        public class GpuAdapterInfo {
            public int Index { get; set; }
            public string Name { get; set; }
            public double DedicatedMb { get; set; }
            public double DedicatedBudgetMb { get; set; }
            public double SharedMb { get; set; }
            public double SharedBudgetMb { get; set; }
            public long Luid { get; set; }
            public uint Flags { get; set; }

            public override string ToString() {
                return string.Format("{0} ({1:F1} GB Dedicated / {2:F1} GB Shared)",
                    Name, DedicatedMb / 1024.0, SharedMb / 1024.0);
            }
        }

        public static List<GpuAdapterInfo> GetAllGpuAdapters() {
            List<GpuAdapterInfo> list = new List<GpuAdapterInfo>();
            Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            IDXGIFactory1 factory;
            if (CreateDXGIFactory1(ref iid, out factory) != 0) return list;

            for (uint i = 0; i < 8; i++) {
                IntPtr pAd;
                if (factory.EnumAdapters1(i, out pAd) != 0) break;
                IDXGIAdapter1 ad1 = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(pAd);
                DXGI_ADAPTER_DESC1 desc;
                ad1.GetDesc1(out desc);

                // Skip software emulation adapters like Microsoft Basic Render Driver
                if ((desc.Flags & 2) != 0) {
                    Marshal.Release(pAd);
                    continue;
                }

                GpuAdapterInfo info = new GpuAdapterInfo();
                info.Index = (int)i;
                info.Name = desc.Description;
                info.DedicatedMb = (ulong)desc.DedicatedVideoMemory / 1048576.0;
                info.DedicatedBudgetMb = info.DedicatedMb;
                info.SharedMb = (ulong)desc.SharedSystemMemory / 1048576.0;
                info.SharedBudgetMb = info.SharedMb;
                info.Luid = desc.AdapterLuid;
                info.Flags = desc.Flags;

                IDXGIAdapter3 a3 = Marshal.GetObjectForIUnknown(pAd) as IDXGIAdapter3;
                if (a3 != null) {
                    DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nonLocVmi;
                    if (a3.QueryVideoMemoryInfo(0, 0, out locVmi) == 0 && locVmi.Budget > 0) {
                        info.DedicatedBudgetMb = locVmi.Budget / 1048576.0;
                    }
                    if (a3.QueryVideoMemoryInfo(0, 1, out nonLocVmi) == 0 && nonLocVmi.Budget > 0) {
                        info.SharedBudgetMb = nonLocVmi.Budget / 1048576.0;
                    }
                }
                list.Add(info);
                Marshal.Release(pAd);
            }
            return list;
        }

        public static void RunFullBenchmark(int targetAdapterIndex, Action<string> logCallback, Action<int> progressCallback, Action onFinished) {
            CancelRequested = false;
            IntPtr targetAdapterPtr = IntPtr.Zero;
            IntPtr ctx = IntPtr.Zero;
            ID3D11Device dev = null;
            GCHandle pinnedHandle = default(GCHandle);
            List<IntPtr> buffers = new List<IntPtr>();

            try {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                IDXGIFactory1 factory;
                CreateDXGIFactory1(ref iid, out factory);

                string targetDesc = "";
                ulong dedicatedBudget = 0;
                ulong sharedBudget = 0;

                if (targetAdapterIndex >= 0) {
                    if (factory.EnumAdapters1((uint)targetAdapterIndex, out targetAdapterPtr) == 0) {
                        IDXGIAdapter1 ad1 = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(targetAdapterPtr);
                        DXGI_ADAPTER_DESC1 desc;
                        ad1.GetDesc1(out desc);
                        targetDesc = desc.Description;
                        dedicatedBudget = (ulong)desc.DedicatedVideoMemory;
                        sharedBudget = (ulong)desc.SharedSystemMemory;
                    }
                }

                if (targetAdapterPtr == IntPtr.Zero) {
                    ulong maxVram = 0;
                    for (uint i = 0; i < 8; i++) {
                        IntPtr pAd;
                        if (factory.EnumAdapters1(i, out pAd) != 0) break;
                        IDXGIAdapter1 ad1 = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(pAd);
                        DXGI_ADAPTER_DESC1 desc;
                        ad1.GetDesc1(out desc);
                        if ((desc.Flags & 2) != 0) { Marshal.Release(pAd); continue; }
                        ulong vram = (ulong)desc.DedicatedVideoMemory;
                        if (vram > maxVram) {
                            maxVram = vram;
                            targetAdapterPtr = pAd;
                            targetDesc = desc.Description;
                            dedicatedBudget = vram;
                            sharedBudget = (ulong)desc.SharedSystemMemory;
                        } else {
                            Marshal.Release(pAd);
                        }
                    }
                }

                if (targetAdapterPtr == IntPtr.Zero) {
                    logCallback("Error: No graphics adapter found.");
                    return;
                }

                IDXGIAdapter3 a3 = Marshal.GetObjectForIUnknown(targetAdapterPtr) as IDXGIAdapter3;
                if (a3 != null) {
                    DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nlocVmi;
                    if (a3.QueryVideoMemoryInfo(0, 0, out locVmi) == 0 && locVmi.Budget > 0) {
                        dedicatedBudget = locVmi.Budget;
                    }
                    if (a3.QueryVideoMemoryInfo(0, 1, out nlocVmi) == 0 && nlocVmi.Budget > 0) {
                        sharedBudget = nlocVmi.Budget;
                    }
                }

                ulong totalMaxBytes = dedicatedBudget + sharedBudget;
                double totalMaxGiB = totalMaxBytes / (1024.0 * 1024.0 * 1024.0);
                double dedicatedGiB = dedicatedBudget / (1024.0 * 1024.0 * 1024.0);
                double sharedGiB = sharedBudget / (1024.0 * 1024.0 * 1024.0);

                logCallback(string.Format("Target GPU Device: {0}", targetDesc));
                logCallback(string.Format("Dedicated VRAM Capacity: {0:F2} GiB ({1:N0} MiB)", dedicatedGiB, dedicatedBudget / 1048576));
                logCallback(string.Format("Shared System RAM Limit: {0:F2} GiB ({1:N0} MiB)", sharedGiB, sharedBudget / 1048576));
                logCallback(string.Format("-> Full Combined Max Pool: {0:F2} GiB ({1:N0} MiB)", totalMaxGiB, totalMaxBytes / 1048576));
                logCallback("--------------------------------------------------");

                int featLevel;
                int hr = D3D11CreateDevice(targetAdapterPtr, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, out dev, out featLevel, out ctx);
                if (hr != 0 || dev == null) {
                    logCallback(string.Format("Failed to initialize Direct3D 11 device (hr=0x{0:X8})", hr));
                    return;
                }

                uint chunkSize = 64 * 1024 * 1024; // 64 MiB chunks
                byte[] initialBytes = new byte[chunkSize];
                for (int i = 0; i < initialBytes.Length; i += 4096) initialBytes[i] = 0x5A; // touch pages to commit
                pinnedHandle = GCHandle.Alloc(initialBytes, GCHandleType.Pinned);

                D3D11_SUBRESOURCE_DATA srd = new D3D11_SUBRESOURCE_DATA();
                srd.pSysMem = pinnedHandle.AddrOfPinnedObject();
                srd.SysMemPitch = chunkSize;
                srd.SysMemSlicePitch = chunkSize;

                // Phase 1 Descriptor: Direct Local Video Memory (Dedicated VRAM)
                D3D11_BUFFER_DESC bdDedicated = new D3D11_BUFFER_DESC();
                bdDedicated.ByteWidth = chunkSize;
                bdDedicated.Usage = 0; // D3D11_USAGE_DEFAULT -> DXGI_MEMORY_SEGMENT_GROUP_LOCAL
                bdDedicated.BindFlags = 0x8; // D3D11_BIND_UNORDERED_ACCESS
                bdDedicated.CPUAccessFlags = 0;
                bdDedicated.MiscFlags = 0;
                bdDedicated.StructureByteStride = 0;

                // Phase 2 Descriptor: Non-Local Shared System RAM over PCIe bus (Reflected in Windows Task Manager)
                D3D11_BUFFER_DESC bdShared = new D3D11_BUFFER_DESC();
                bdShared.ByteWidth = chunkSize;
                bdShared.Usage = 3; // D3D11_USAGE_STAGING -> DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL
                bdShared.BindFlags = 0;
                bdShared.CPUAccessFlags = 0x30000; // D3D11_CPU_ACCESS_READ | D3D11_CPU_ACCESS_WRITE
                bdShared.MiscFlags = 0;
                bdShared.StructureByteStride = 0;

                FlushDelegate flush = null;
                if (ctx != IntPtr.Zero) {
                    try {
                        IntPtr vtbl = Marshal.ReadIntPtr(ctx);
                        IntPtr flushPtr = Marshal.ReadIntPtr(vtbl, 111 * IntPtr.Size);
                        flush = (FlushDelegate)Marshal.GetDelegateForFunctionPointer(flushPtr, typeof(FlushDelegate));
                    } catch { }
                }

                ulong totalAllocated = 0;
                bool enteredShared = (dedicatedBudget == 0);

                logCallback("Allocating GPU-resident memory blocks (64 MiB each)...");
                while (totalAllocated < totalMaxBytes) {
                    if (CancelRequested) {
                        logCallback("-> Allocation halted by user.");
                        break;
                    }

                    bool isPhase1 = (totalAllocated < dedicatedBudget);

                    // Set WDDM reservation to keep working set resident
                    if (isPhase1 && a3 != null) {
                        try {
                            ulong targetLocal = Math.Min(totalAllocated + chunkSize, dedicatedBudget);
                            a3.SetVideoMemoryReservation(0, 0, targetLocal);
                        } catch { }
                    }

                    IntPtr buf;
                    int bhr;
                    if (isPhase1) {
                        bhr = dev.CreateBuffer(ref bdDedicated, ref srd, out buf);
                        if (bhr != 0 || buf == IntPtr.Zero) {
                            bhr = dev.CreateBufferNull(ref bdDedicated, IntPtr.Zero, out buf);
                        }
                    } else {
                        if (!enteredShared) {
                            enteredShared = true;
                            if (flush != null) { try { flush(ctx); } catch { } }
                            logCallback(string.Format(">> [PHASE 1 COMPLETE] Dedicated VRAM ({0:F2} GiB) 100% saturated!", dedicatedGiB));
                            logCallback(">> [PHASE 2 ACTIVE] Allocating into Shared GPU Memory via PCIe aperture (Reflected in Task Manager)...");
                        }
                        bhr = dev.CreateBuffer(ref bdShared, ref srd, out buf);
                        if (bhr != 0 || buf == IntPtr.Zero) {
                            bhr = dev.CreateBufferNull(ref bdShared, IntPtr.Zero, out buf);
                        }
                    }

                    if (bhr != 0 || buf == IntPtr.Zero) {
                        logCallback(string.Format("-> Driver reached hardware capacity at {0:F2} GiB ({1:N0} MiB).", totalAllocated / 1073741824.0, totalAllocated / 1048576));
                        break;
                    }
                    buffers.Add(buf);
                    totalAllocated += chunkSize;

                    // Flush GPU pipeline and report progress every 1 GiB or upon hitting pool max
                    if (totalAllocated % (1024UL * 1024 * 1024) == 0 || totalAllocated >= totalMaxBytes) {
                        if (flush != null) {
                            try { flush(ctx); } catch { }
                        }

                        int pct = (int)((totalAllocated * 100UL) / Math.Max(1UL, totalMaxBytes));
                        if (pct > 100) pct = 100;
                        progressCallback(pct);

                        // Query live driver hardware telemetry
                        ulong liveDedicatedUsage = 0;
                        ulong liveSharedUsage = 0;
                        bool hasLiveTelemetry = false;
                        if (a3 != null) {
                            DXGI_QUERY_VIDEO_MEMORY_INFO locVmi, nlocVmi;
                            if (a3.QueryVideoMemoryInfo(0, 0, out locVmi) == 0 &&
                                a3.QueryVideoMemoryInfo(0, 1, out nlocVmi) == 0) {
                                liveDedicatedUsage = locVmi.CurrentUsage;
                                liveSharedUsage = nlocVmi.CurrentUsage;
                                hasLiveTelemetry = true;
                            }
                        }

                        ulong curDedicated = Math.Min(totalAllocated, dedicatedBudget);
                        ulong curShared = totalAllocated > dedicatedBudget ? (totalAllocated - dedicatedBudget) : 0;
                        double curDedGiB = (hasLiveTelemetry && liveDedicatedUsage > 0 ? liveDedicatedUsage : curDedicated) / 1073741824.0;
                        double curSharedGiB = (hasLiveTelemetry && liveSharedUsage > 0 ? liveSharedUsage : curShared) / 1073741824.0;
                        double dedPct = (dedicatedBudget > 0) ? (curDedGiB * 1073741824.0 * 100.0 / dedicatedBudget) : 0;
                        double shPct = (sharedBudget > 0) ? (curSharedGiB * 1073741824.0 * 100.0 / sharedBudget) : 0;

                        string phaseTag = isPhase1 ? "[PHASE 1 - DEDICATED VRAM]" : "[PHASE 2 - SHARED GPU RAM]";

                        logCallback(string.Format("  -> {0} Committed: {1:F2} / {2:F2} GiB ({3}%) | Dedicated: {4:F2} GiB ({5:F0}%) | Shared Memory: {6:F2} GiB ({7:F0}%)",
                            phaseTag, totalAllocated / 1073741824.0, totalMaxGiB, pct, curDedGiB, dedPct, curSharedGiB, shPct));
                    }
                }

                progressCallback(100);

                logCallback("--------------------------------------------------");
                logCallback(string.Format("★ BENCHMARK FINISHED: Successfully allocated {0:F2} GiB ({1:N0} MiB)!", totalAllocated / 1073741824.0, totalAllocated / 1048576));
                logCallback(string.Format("Created {0} resident Direct3D 11 buffers across Dedicated VRAM + Shared GPU Memory.", buffers.Count));
                logCallback("--------------------------------------------------");
            } catch (Exception ex) {
                logCallback("Benchmark encountered an unexpected error: " + ex.Message);
            } finally {
                logCallback("Releasing all allocated buffers...");
                if (pinnedHandle.IsAllocated) {
                    try { pinnedHandle.Free(); } catch { }
                }
                foreach (var b in buffers) {
                    try { Marshal.Release(b); } catch { }
                }
                buffers.Clear();
                if (ctx != IntPtr.Zero) {
                    try { Marshal.Release(ctx); } catch { }
                }
                if (targetAdapterPtr != IntPtr.Zero) {
                    try { Marshal.Release(targetAdapterPtr); } catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();

                logCallback("✔ All buffers freed. All memory safely returned to Windows.");
                onFinished();
            }
        }
    }
    #endregion

    public class VRAMOptimizerWindow {
        private const string MM_PATH = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MemoryManager";
        private const string GD_PATH = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public Window Window { get; private set; }

        private bool isDarkMode = true;

        private Button btnThemeToggle;
        private TextBlock lblLiveVramBudget;
        private TextBlock lblLiveVramGain;
        private TextBlock lblLiveWddmReserve;
        private TextBlock lblLiveWddmState;
        private TextBlock lblLiveSharedRam;
        private TextBlock lblLiveSharedGb;
        private ComboBox cmbGpuSelector;
        private TextBlock lblLiveTotalPool;

        private Button btnPresetRecommended;
        private Button btnPresetAggressive;
        private Button btnPresetDefault;
        private Button btnExportBackup;
        private Button btnRestoreBackup;

        // Sliders & Live Badges
        private TextBlock lblFragValue;
        private Slider sldFragBuffer;
        private TextBlock lblProcValue;
        private Slider sldProcCap;
        private TextBlock lblSharedValue;
        private Slider sldSharedRam;
        private ComboBox cmbHags;

        // Benchmark Controls
        private TextBlock lblBenchGpuName;
        private TextBlock lblBenchDedicated;
        private TextBlock lblBenchShared;
        private TextBlock lblBenchCombined;
        private Button btnRunVramTest;
        private Button btnStopVramTest;
        private ProgressBar pbVramTest;
        private TextBox txtTestLog;

        private Button btnRefresh;
        private Button btnApply;
        private Button btnReboot;
        private TextBlock txtActionNotice;

        // Multi-GPU Support & Dynamic Hardware Limits
        private List<VramBenchmark.GpuAdapterInfo> detectedGpus = new List<VramBenchmark.GpuAdapterInfo>();
        private VramBenchmark.GpuAdapterInfo selectedGpu = null;
        private double liveBudgetMb = 0;
        private double liveSharedBudgetMb = 0;
        private double liveVramMb = 0;
        private double totalRamGb = 32.0;
        private const int STOCK_FRAG_MB = 512;
        private const int STOCK_PROC_MB = 256;
        private const int STOCK_SHARED_PCT = 50;

        public VRAMOptimizerWindow() {
            string xaml = @"
<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Windows WDDM &amp; VRAM Optimizer"" Height=""860"" Width=""920""
        WindowStartupLocation=""CenterScreen"" ResizeMode=""CanMinimize""
        Background=""{DynamicResource BgBrush}"" Foreground=""{DynamicResource TextBrush}"" FontFamily=""Segoe UI"">
    
    <Window.Resources>
        <SolidColorBrush x:Key=""BgBrush"" Color=""#0B1120""/>
        <SolidColorBrush x:Key=""CardBrush"" Color=""#1E293B""/>
        <SolidColorBrush x:Key=""CardBorderBrush"" Color=""#334155""/>
        <SolidColorBrush x:Key=""TextBrush"" Color=""#F8FAFC""/>
        <SolidColorBrush x:Key=""SubTextBrush"" Color=""#94A3B8""/>
        <SolidColorBrush x:Key=""HeaderBrush"" Color=""#FFFFFF""/>
        <SolidColorBrush x:Key=""InputBgBrush"" Color=""#0F172A""/>
        <SolidColorBrush x:Key=""InputFgBrush"" Color=""#F8FAFC""/>
        <SolidColorBrush x:Key=""InputBorderBrush"" Color=""#475569""/>
        <SolidColorBrush x:Key=""ConsoleBgBrush"" Color=""#020617""/>
        <SolidColorBrush x:Key=""ConsoleFgBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""CardTagDxgiBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""CardTagWddmBrush"" Color=""#818CF8""/>
        <SolidColorBrush x:Key=""CardTagSharedBrush"" Color=""#34D399""/>
        <SolidColorBrush x:Key=""CardTagGpuBrush"" Color=""#FBBF24""/>
        <SolidColorBrush x:Key=""SectionTitleBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""AccentGreenBrush"" Color=""#4ADE80""/>
        <SolidColorBrush x:Key=""SliderTrackBrush"" Color=""#334155""/>
        <SolidColorBrush x:Key=""SliderThumbBrush"" Color=""#38BDF8""/>

        <!-- Button Style -->
        <Style TargetType=""Button"">
            <Setter Property=""Padding"" Value=""14,8""/>
            <Setter Property=""FontWeight"" Value=""SemiBold""/>
            <Setter Property=""BorderThickness"" Value=""0""/>
            <Setter Property=""Cursor"" Value=""Hand""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Button"">
                        <Border x:Name=""border"" Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""{TemplateBinding Padding}"">
                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsMouseOver"" Value=""True"">
                                <Setter Property=""Opacity"" Value=""0.88""/>
                            </Trigger>
                            <Trigger Property=""IsPressed"" Value=""True"">
                                <Setter Property=""Opacity"" Value=""0.75""/>
                            </Trigger>
                            <Trigger Property=""IsEnabled"" Value=""False"">
                                <Setter Property=""Opacity"" Value=""0.45""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Thumb Style -->
        <Style x:Key=""SliderThumbStyle"" TargetType=""Thumb"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Thumb"">
                        <Ellipse Width=""18"" Height=""18"" Fill=""{DynamicResource SliderThumbBrush}"" Stroke=""#FFFFFF"" StrokeThickness=""2"" Cursor=""Hand""/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Template Style -->
        <Style TargetType=""Slider"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
            <Setter Property=""VerticalAlignment"" Value=""Center""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Slider"">
                        <Grid VerticalAlignment=""Center"">
                            <Border Height=""6"" Background=""{DynamicResource SliderTrackBrush}"" CornerRadius=""3"" VerticalAlignment=""Center""/>
                            <Track x:Name=""PART_Track"">
                                <Track.Thumb>
                                    <Thumb Style=""{StaticResource SliderThumbStyle}""/>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBoxItem Style -->
        <Style TargetType=""ComboBoxItem"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""Padding"" Value=""10,6""/>
            <Setter Property=""HorizontalContentAlignment"" Value=""Left""/>
            <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
            <Setter Property=""Background"" Value=""Transparent""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource InputFgBrush}""/>
            <Setter Property=""BorderThickness"" Value=""0""/>
            <Setter Property=""Cursor"" Value=""Hand""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBoxItem"">
                        <Border x:Name=""ItemBorder"" Background=""{TemplateBinding Background}"" CornerRadius=""4"" Padding=""{TemplateBinding Padding}"" Margin=""1"">
                            <ContentPresenter x:Name=""ItemContent"" 
                                              HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" 
                                              VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""
                                              TextElement.Foreground=""{TemplateBinding Foreground}""
                                              TextElement.FontSize=""12""/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsHighlighted"" Value=""True"">
                                <Setter TargetName=""ItemBorder"" Property=""Background"" Value=""#2563EB""/>
                                <Setter Property=""Foreground"" Value=""#FFFFFF""/>
                            </Trigger>
                            <Trigger Property=""IsSelected"" Value=""True"">
                                <Setter TargetName=""ItemBorder"" Property=""Background"" Value=""#1D4ED8""/>
                                <Setter Property=""Foreground"" Value=""#FFFFFF""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBox ToggleButton Template -->
        <ControlTemplate x:Key=""ComboBoxToggleButtonTemplate"" TargetType=""ToggleButton"">
            <Border x:Name=""templateRoot"" 
                    Background=""{DynamicResource InputBgBrush}"" 
                    BorderBrush=""{DynamicResource InputBorderBrush}"" 
                    BorderThickness=""1"" 
                    CornerRadius=""6"">
                <Border x:Name=""splitBorder"" Width=""30"" HorizontalAlignment=""Right"">
                    <Path x:Name=""arrow"" 
                          Data=""M 0 0 L 4.5 4.5 L 9 0 Z"" 
                          Fill=""{DynamicResource InputFgBrush}"" 
                          HorizontalAlignment=""Center"" 
                          VerticalAlignment=""Center""/>
                </Border>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property=""IsMouseOver"" Value=""True"">
                    <Setter TargetName=""templateRoot"" Property=""BorderBrush"" Value=""#38BDF8""/>
                </Trigger>
                <Trigger Property=""IsChecked"" Value=""True"">
                    <Setter TargetName=""templateRoot"" Property=""BorderBrush"" Value=""#2563EB""/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>

        <!-- Custom ComboBox Style -->
        <Style TargetType=""ComboBox"">
            <Setter Property=""Foreground"" Value=""{DynamicResource InputFgBrush}""/>
            <Setter Property=""Background"" Value=""{DynamicResource InputBgBrush}""/>
            <Setter Property=""BorderBrush"" Value=""{DynamicResource InputBorderBrush}""/>
            <Setter Property=""BorderThickness"" Value=""1""/>
            <Setter Property=""FontSize"" Value=""12.5""/>
            <Setter Property=""FontWeight"" Value=""Medium""/>
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBox"">
                        <Grid x:Name=""templateRoot"">
                            <ToggleButton x:Name=""toggleButton"" 
                                          Template=""{StaticResource ComboBoxToggleButtonTemplate}"" 
                                          Focusable=""False"" 
                                          IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" 
                                          ClickMode=""Press""/>
                            <ContentPresenter x:Name=""contentPresenter"" 
                                              Content=""{TemplateBinding SelectionBoxItem}"" 
                                              ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}"" 
                                              ContentTemplateSelector=""{TemplateBinding ItemTemplateSelector}"" 
                                              Margin=""10,0,32,0"" 
                                              HorizontalAlignment=""Left"" 
                                              VerticalAlignment=""Center"" 
                                              IsHitTestVisible=""False""
                                              TextElement.Foreground=""{DynamicResource InputFgBrush}""
                                              TextElement.FontSize=""12""
                                              TextElement.FontWeight=""SemiBold""/>
                            <Popup x:Name=""PART_Popup"" 
                                   AllowsTransparency=""True"" 
                                   IsOpen=""{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}"" 
                                   Placement=""Bottom"" 
                                   PopupAnimation=""Fade"">
                                <Border x:Name=""dropDownBorder"" 
                                        Background=""{DynamicResource CardBrush}"" 
                                        BorderBrush=""{DynamicResource CardBorderBrush}"" 
                                        BorderThickness=""1"" 
                                        CornerRadius=""6"" 
                                        Margin=""0,3,0,0"" 
                                        Padding=""4"" 
                                        MinWidth=""{Binding ActualWidth, ElementName=templateRoot}"" 
                                        MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                                    <ScrollViewer x:Name=""DropDownScrollViewer"" CanContentScroll=""True"">
                                        <ItemsPresenter x:Name=""ItemsPresenter"" KeyboardNavigation.DirectionalNavigation=""Contained""/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin=""20"">
        <Grid.RowDefinitions>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""*""/>
            <RowDefinition Height=""Auto""/>
        </Grid.RowDefinitions>

        <!-- Top Header & Theme Switcher -->
        <Grid Grid.Row=""0"" Margin=""0,0,0,14"">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""Auto""/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column=""0"">
                <TextBlock Text=""Windows WDDM &amp; VRAM Optimizer"" FontSize=""23"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                <TextBlock Text=""Dynamic GPU-tailored VRAM optimization, PCIe Shared RAM allocation &amp; Safe Registry Management"" FontSize=""13"" Foreground=""{DynamicResource SubTextBrush}"" Margin=""0,3,0,0""/>
            </StackPanel>
            <Button x:Name=""btnThemeToggle"" Grid.Column=""1"" Content=""☀️ Light Mode"" Background=""#334155"" Foreground=""#F8FAFC"" VerticalAlignment=""Center"" Padding=""12,7"" ToolTip=""Toggle between Dark Mode and Light Mode""/>
        </Grid>

        <!-- 4 Live Status Cards (Dynamic Hardware Pool) -->
        <Grid Grid.Row=""1"" Margin=""0,0,0,14"">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""1.25*""/>
            </Grid.ColumnDefinitions>

            <!-- Card 1: Dedicated Budget -->
            <Border Grid.Column=""0"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12"" Margin=""0,0,5,0"">
                <StackPanel>
                    <TextBlock Text=""DXGI DEDICATED BUDGET"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagDxgiBrush}""/>
                    <TextBlock x:Name=""lblLiveVramBudget"" Text=""5,760 MiB"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveVramGain"" Text=""+607 MiB Unlocked"" FontSize=""11"" Foreground=""{DynamicResource AccentGreenBrush}"" Margin=""0,2,0,0""/>
                </StackPanel>
            </Border>

            <!-- Card 2: WDDM Reserve -->
            <Border Grid.Column=""1"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12"" Margin=""4,0,4,0"">
                <StackPanel>
                    <TextBlock Text=""WDDM KERNEL RESERVE"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagWddmBrush}""/>
                    <TextBlock x:Name=""lblLiveWddmReserve"" Text=""160 MiB"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveWddmState"" Text=""Optimized (128+32)"" FontSize=""11"" Foreground=""{DynamicResource CardTagWddmBrush}"" Margin=""0,2,0,0""/>
                </StackPanel>
            </Border>

            <!-- Card 3: Shared RAM Cap -->
            <Border Grid.Column=""2"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12"" Margin=""4,0,4,0"">
                <StackPanel>
                    <TextBlock Text=""SHARED SYSTEM RAM CAP"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagSharedBrush}""/>
                    <TextBlock x:Name=""lblLiveSharedRam"" Text=""75%"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveSharedGb"" Text=""~24.0 GB Limit"" FontSize=""11"" Foreground=""{DynamicResource CardTagSharedBrush}"" Margin=""0,2,0,0""/>
                </StackPanel>
            </Border>

            <!-- Card 4: Active GPU Adapter & Combined Max Pool -->
            <Border Grid.Column=""3"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""10,8"" Margin=""5,0,0,0"">
                <StackPanel>
                    <Grid Margin=""0,0,0,2"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column=""0"" Text=""SELECT GPU ADAPTER"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagGpuBrush}""/>
                        <TextBlock Grid.Column=""1"" Text=""Multi-GPU"" FontSize=""9"" FontWeight=""SemiBold"" Foreground=""{DynamicResource SubTextBrush}""/>
                    </Grid>
                    <ComboBox x:Name=""cmbGpuSelector"" Height=""28"" Margin=""0,2,0,3"" ToolTip=""Select which GPU adapter to monitor, optimize, and benchmark""/>
                    <TextBlock x:Name=""lblLiveTotalPool"" Text=""Total Pool: ~30.8 GiB"" FontSize=""10.5"" Foreground=""{DynamicResource AccentGreenBrush}"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Main Body: Presets, Dynamic Sliders, Full Memory Benchmark -->
        <ScrollViewer Grid.Row=""2"" VerticalScrollBarVisibility=""Auto"">
            <StackPanel Margin=""0,0,6,0"">

                <!-- One-Click Presets & Backup Controls -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""14"" Margin=""0,0,0,12"">
                    <StackPanel>
                        <Grid Margin=""0,0,0,10"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column=""0"" Text=""ONE-CLICK OPTIMIZATION PRESETS &amp; SAFE BACKUP"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}"" VerticalAlignment=""Center""/>
                            <StackPanel Grid.Column=""1"" Orientation=""Horizontal"">
                                <Button x:Name=""btnExportBackup"" Content=""💾 Export .reg Backup"" Background=""#334155"" Foreground=""#F8FAFC"" Padding=""10,5"" Margin=""0,0,6,0"" FontSize=""11"" ToolTip=""Export current registry settings to a .reg file for safe backup""/>
                                <Button x:Name=""btnRestoreBackup"" Content=""📂 Restore from .reg"" Background=""#334155"" Foreground=""#F8FAFC"" Padding=""10,5"" FontSize=""11"" ToolTip=""Restore previously exported .reg backup file""/>
                            </StackPanel>
                        </Grid>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name=""btnPresetRecommended"" Grid.Column=""0"" Content=""★ Recommended (Optimal)"" Background=""#2563EB"" Foreground=""White"" Margin=""0,0,5,0"" ToolTip=""Applies 128MB frag buffer, 32MB process cap, and 75% Shared RAM (recovers +608MB VRAM).""/>
                            <Button x:Name=""btnPresetAggressive"" Grid.Column=""1"" Content=""⚡ Aggressive (AI / LLM)"" Background=""#7C3AED"" Foreground=""White"" Margin=""5,0,5,0"" ToolTip=""Applies 32MB minimum hardware floor and 80% Shared RAM for maximum headroom.""/>
                            <Button x:Name=""btnPresetDefault"" Grid.Column=""2"" Content=""↺ Windows Stock Defaults"" Background=""#475569"" Foreground=""#F8FAFC"" Margin=""5,0,0,0"" ToolTip=""Removes custom overrides and restores stock Windows driver defaults.""/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Dynamic Fine-Grained Sliders Card -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,12"">
                    <StackPanel>
                        <TextBlock Text=""FINE-GRAINED SLIDERS (DYNAMIC GPU VRAM SIZING)"" FontWeight=""Bold"" FontSize=""14"" Foreground=""{DynamicResource SectionTitleBrush}"" Margin=""0,0,0,14""/>
                        
                        <!-- Slider 1: Fragmentation Buffer -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Max Video Memory Fragmentation Buffer"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Kernel page-table &amp; defragmentation reserve. Lower values unlock dedicated VRAM."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblFragValue"" Text=""512 MiB (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldFragBuffer"" Grid.Row=""1"" Height=""26"" Minimum=""32"" Maximum=""512"" Value=""512"" TickFrequency=""32"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- Slider 2: Process Budget Cap -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Max Process Budget Cap Buffer"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Foreground application throttle cap for desktop/DWM. Lower values unlock dedicated VRAM."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblProcValue"" Text=""256 MiB (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldProcCap"" Grid.Row=""1"" Height=""26"" Minimum=""32"" Maximum=""256"" Value=""256"" TickFrequency=""32"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- Slider 3: Shared RAM Commit Limit -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Shared GPU Memory Commit Limit (System RAM)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Maximum host physical RAM mapped to GPU over PCIe bus. Dynamically computed from system RAM."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblSharedValue"" Text=""50% (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldSharedRam"" Grid.Row=""1"" Height=""26"" Minimum=""50"" Maximum=""90"" Value=""50"" TickFrequency=""5"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- HAGS (Clean Toggle, Wide Column to prevent truncation) -->
                        <Grid Margin=""0,2,0,0"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""310""/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column=""0"">
                                <TextBlock Text=""Hardware-Accelerated GPU Scheduling (HAGS)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                <TextBlock Text=""OFF saves 40-100MB VRAM for CUDA/LLMs; ON required for DLSS Frame Gen."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                            </StackPanel>
                            <ComboBox x:Name=""cmbHags"" Grid.Column=""1"" Height=""32"" VerticalAlignment=""Center""/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Live VRAM + Shared RAM Allocation Benchmark (Automatic Sizing) -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,10"">
                    <StackPanel>
                        <Grid Margin=""0,0,0,3"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column=""0"" Text=""LIVE VRAM + SHARED RAM ALLOCATION BENCHMARK"" FontWeight=""Bold"" FontSize=""14"" Foreground=""{DynamicResource AccentGreenBrush}""/>
                            <TextBlock x:Name=""lblBenchGpuName"" Grid.Column=""1"" Text=""GPU: RTX 4050"" FontSize=""11.5"" FontWeight=""SemiBold"" Foreground=""{DynamicResource CardTagGpuBrush}""/>
                        </Grid>
                        <TextBlock Text=""Two-phase hardware allocator: Phase 1 saturates Dedicated VRAM; Phase 2 allocates PCIe Staging buffers into Shared GPU Memory (reflected 1:1 in Windows Task Manager). Zero manual sizing required."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}"" Margin=""0,0,0,12""/>

                        <!-- Automatic Target Metrics -->
                        <Grid Margin=""0,0,0,12"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""1.2*""/>
                            </Grid.ColumnDefinitions>

                            <Border Grid.Column=""0"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""0,0,5,0"">
                                <StackPanel>
                                    <TextBlock Text=""DEDICATED VRAM TARGET"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagDxgiBrush}""/>
                                    <TextBlock x:Name=""lblBenchDedicated"" Text=""5,760 MiB (5.6 GiB)"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""4,0,4,0"">
                                <StackPanel>
                                    <TextBlock Text=""SHARED RAM TARGET"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagSharedBrush}""/>
                                    <TextBlock x:Name=""lblBenchShared"" Text=""25,052 MiB (24.5 GiB)"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column=""2"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""5,0,0,0"">
                                <StackPanel>
                                    <TextBlock Text=""MAX COMBINED POOL"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource AccentGreenBrush}""/>
                                    <TextBlock x:Name=""lblBenchCombined"" Text=""30,812 MiB (~30.1 GiB)"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource AccentGreenBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>
                        </Grid>

                        <!-- Action Buttons -->
                        <Grid Margin=""0,0,0,10"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name=""btnRunVramTest"" Grid.Column=""0"" Content=""▶ Run Full Memory Benchmark (Dedicated + Shared Pool)"" Background=""#16A34A"" Foreground=""White"" Padding=""16,8"" Margin=""0,0,8,0""/>
                            <Button x:Name=""btnStopVramTest"" Grid.Column=""1"" Content=""■ Stop"" Background=""#DC2626"" Foreground=""White"" Padding=""16,8"" IsEnabled=""False""/>
                        </Grid>

                        <ProgressBar x:Name=""pbVramTest"" Height=""8"" Background=""{DynamicResource InputBgBrush}"" Foreground=""#16A34A"" Value=""0"" Margin=""0,0,0,8""/>

                        <Border Background=""{DynamicResource ConsoleBgBrush}"" CornerRadius=""6"" Padding=""10"">
                            <TextBox x:Name=""txtTestLog"" Text=""Ready to test. Maximum capacity is automatically set to your full combined hardware pool. Click 'Run Full Memory Benchmark' to begin."" FontFamily=""Consolas"" FontSize=""11.5"" Foreground=""{DynamicResource ConsoleFgBrush}"" Background=""Transparent"" BorderThickness=""0"" IsReadOnly=""True"" TextWrapping=""Wrap"" MaxHeight=""135"" VerticalScrollBarVisibility=""Auto""/>
                        </Border>
                    </StackPanel>
                </Border>

            </StackPanel>
        </ScrollViewer>

        <!-- Bottom Action Bar -->
        <Border Grid.Row=""3"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12"" Margin=""0,12,0,0"">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""Auto""/>
                    <ColumnDefinition Width=""*""/>
                    <ColumnDefinition Width=""Auto""/>
                    <ColumnDefinition Width=""Auto""/>
                </Grid.ColumnDefinitions>
                <Button x:Name=""btnRefresh"" Grid.Column=""0"" Content=""Refresh Live Status (F5)"" Background=""#475569"" Foreground=""White"" Margin=""0,0,8,0""/>
                <TextBlock x:Name=""txtActionNotice"" Grid.Column=""1"" VerticalAlignment=""Center"" Margin=""10,0"" Foreground=""{DynamicResource AccentGreenBrush}"" FontWeight=""SemiBold"" Text=""""/>
                <Button x:Name=""btnApply"" Grid.Column=""2"" Content=""Apply Settings (Ctrl+S)"" Background=""#2563EB"" Foreground=""White"" Margin=""0,0,8,0""/>
                <Button x:Name=""btnReboot"" Grid.Column=""3"" Content=""Restart Windows Now"" Background=""#DC2626"" Foreground=""White""/>
            </Grid>
        </Border>
    </Grid>
</Window>";

            XmlReader reader = XmlReader.Create(new StringReader(xaml));
            Window = (Window)XamlReader.Load(reader);

            // Hook named elements directly from Window's NameScope
            btnThemeToggle = (Button)Window.FindName("btnThemeToggle");
            lblLiveVramBudget = (TextBlock)Window.FindName("lblLiveVramBudget");
            lblLiveVramGain = (TextBlock)Window.FindName("lblLiveVramGain");
            lblLiveWddmReserve = (TextBlock)Window.FindName("lblLiveWddmReserve");
            lblLiveWddmState = (TextBlock)Window.FindName("lblLiveWddmState");
            lblLiveSharedRam = (TextBlock)Window.FindName("lblLiveSharedRam");
            lblLiveSharedGb = (TextBlock)Window.FindName("lblLiveSharedGb");
            cmbGpuSelector = (ComboBox)Window.FindName("cmbGpuSelector");
            lblLiveTotalPool = (TextBlock)Window.FindName("lblLiveTotalPool");

            btnPresetRecommended = (Button)Window.FindName("btnPresetRecommended");
            btnPresetAggressive = (Button)Window.FindName("btnPresetAggressive");
            btnPresetDefault = (Button)Window.FindName("btnPresetDefault");
            btnExportBackup = (Button)Window.FindName("btnExportBackup");
            btnRestoreBackup = (Button)Window.FindName("btnRestoreBackup");

            lblFragValue = (TextBlock)Window.FindName("lblFragValue");
            sldFragBuffer = (Slider)Window.FindName("sldFragBuffer");
            lblProcValue = (TextBlock)Window.FindName("lblProcValue");
            sldProcCap = (Slider)Window.FindName("sldProcCap");
            lblSharedValue = (TextBlock)Window.FindName("lblSharedValue");
            sldSharedRam = (Slider)Window.FindName("sldSharedRam");
            cmbHags = (ComboBox)Window.FindName("cmbHags");
            cmbHags.Items.Add("Disabled (Saves 40-100MB / HwSchMode=1)");
            cmbHags.Items.Add("Enabled (Required for Frame Gen / HwSchMode=2)");
            cmbHags.SelectedIndex = 1;

            lblBenchGpuName = (TextBlock)Window.FindName("lblBenchGpuName");
            lblBenchDedicated = (TextBlock)Window.FindName("lblBenchDedicated");
            lblBenchShared = (TextBlock)Window.FindName("lblBenchShared");
            lblBenchCombined = (TextBlock)Window.FindName("lblBenchCombined");
            btnRunVramTest = (Button)Window.FindName("btnRunVramTest");
            btnStopVramTest = (Button)Window.FindName("btnStopVramTest");
            pbVramTest = (ProgressBar)Window.FindName("pbVramTest");
            txtTestLog = (TextBox)Window.FindName("txtTestLog");

            btnRefresh = (Button)Window.FindName("btnRefresh");
            btnApply = (Button)Window.FindName("btnApply");
            btnReboot = (Button)Window.FindName("btnReboot");
            txtActionNotice = (TextBlock)Window.FindName("txtActionNotice");

            btnThemeToggle.Click += (s, e) => SetTheme(!isDarkMode);

            // Populate Multi-GPU Selector
            totalRamGb = VramBenchmark.GetTotalSystemRamGb();
            detectedGpus = VramBenchmark.GetAllGpuAdapters();
            cmbGpuSelector.ItemsSource = detectedGpus;

            int defaultIndex = 0;
            double maxDed = -1;
            for (int i = 0; i < detectedGpus.Count; i++) {
                if (detectedGpus[i].DedicatedMb > maxDed) {
                    maxDed = detectedGpus[i].DedicatedMb;
                    defaultIndex = i;
                }
            }
            if (detectedGpus.Count > 0) {
                cmbGpuSelector.SelectedIndex = defaultIndex;
                selectedGpu = detectedGpus[defaultIndex];
                liveBudgetMb = (selectedGpu.DedicatedBudgetMb > 0) ? selectedGpu.DedicatedBudgetMb : selectedGpu.DedicatedMb;
                liveVramMb = selectedGpu.DedicatedMb;
                liveSharedBudgetMb = (selectedGpu.SharedBudgetMb > 0) ? selectedGpu.SharedBudgetMb : selectedGpu.SharedMb;
            }

            // Set slider bounds dynamically to Windows WDDM limits
            sldFragBuffer.Minimum = 32;
            sldFragBuffer.Maximum = STOCK_FRAG_MB;
            sldFragBuffer.TickFrequency = 32;

            sldProcCap.Minimum = 32;
            sldProcCap.Maximum = STOCK_PROC_MB;
            sldProcCap.TickFrequency = 32;

            sldSharedRam.Minimum = 50;
            sldSharedRam.Maximum = 90;
            sldSharedRam.TickFrequency = 5;

            // Handle GPU Selection Change (Multi-GPU switching)
            cmbGpuSelector.SelectionChanged += (s, e) => {
                VramBenchmark.GpuAdapterInfo info = cmbGpuSelector.SelectedItem as VramBenchmark.GpuAdapterInfo;
                if (info != null) {
                    selectedGpu = info;
                    liveBudgetMb = (info.DedicatedBudgetMb > 0) ? info.DedicatedBudgetMb : info.DedicatedMb;
                    liveVramMb = info.DedicatedMb;
                    liveSharedBudgetMb = (info.SharedBudgetMb > 0) ? info.SharedBudgetMb : info.SharedMb;
                    UpdateDashboardCards();
                }
            };

            // Dynamic Live Badge Handlers
            sldFragBuffer.ValueChanged += (s, e) => {
                int val = (int)sldFragBuffer.Value;
                int saved = STOCK_FRAG_MB - val;
                if (saved > 0) {
                    lblFragValue.Text = string.Format("{0} MiB (+{1} MiB Unlocked)", val, saved);
                    lblFragValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                } else {
                    lblFragValue.Text = string.Format("{0} MiB (Stock Default)", val);
                    lblFragValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
                }
            };

            sldProcCap.ValueChanged += (s, e) => {
                int val = (int)sldProcCap.Value;
                int saved = STOCK_PROC_MB - val;
                if (saved > 0) {
                    lblProcValue.Text = string.Format("{0} MiB (+{1} MiB Unlocked)", val, saved);
                    lblProcValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                } else {
                    lblProcValue.Text = string.Format("{0} MiB (Stock Default)", val);
                    lblProcValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
                }
            };

            sldSharedRam.ValueChanged += (s, e) => {
                int val = (int)sldSharedRam.Value;
                double gb = Math.Round((val * totalRamGb) / 100.0, 1);
                if (val > STOCK_SHARED_PCT) {
                    lblSharedValue.Text = string.Format("{0}% (~{1:F1} GB on {2:F0}GB RAM / +{3}% Shared)", val, gb, totalRamGb, val - STOCK_SHARED_PCT);
                    lblSharedValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                } else {
                    lblSharedValue.Text = string.Format("{0}% (Stock Default / ~{1:F1} GB on {2:F0}GB RAM)", val, gb, totalRamGb);
                    lblSharedValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
                }
            };

            // Preset Buttons
            btnPresetRecommended.Click += (s, e) => {
                sldFragBuffer.Value = 128;
                sldProcCap.Value = 32;
                sldSharedRam.Value = 75;
                int hags = (cmbHags.SelectedIndex == 0) ? 1 : 2;
                ApplySettings(128, 32, 75, hags);
            };

            btnPresetAggressive.Click += (s, e) => {
                sldFragBuffer.Value = 32;
                sldProcCap.Value = 32;
                sldSharedRam.Value = 80;
                int hags = (cmbHags.SelectedIndex == 0) ? 1 : 2;
                ApplySettings(32, 32, 80, hags);
            };

            btnPresetDefault.Click += (s, e) => {
                sldFragBuffer.Value = STOCK_FRAG_MB;
                sldProcCap.Value = STOCK_PROC_MB;
                sldSharedRam.Value = STOCK_SHARED_PCT;
                cmbHags.SelectedIndex = 1;
                ApplySettings(null, null, null, 2);
            };

            // Registry Backup & Restore Actions
            btnExportBackup.Click += (s, e) => {
                try {
                    SaveFileDialog sfd = new SaveFileDialog();
                    sfd.Filter = "Registry Files (*.reg)|*.reg";
                    sfd.FileName = string.Format("WDDM_VRAM_Backup_{0:yyyyMMdd_HHmmss}.reg", DateTime.Now);
                    sfd.Title = "Save WDDM Registry Settings Backup";
                    if (sfd.ShowDialog() == true) {
                        ExportRegistryFile(sfd.FileName);
                        txtActionNotice.Text = string.Format("Backup saved to {0}", Path.GetFileName(sfd.FileName));
                        MessageBox.Show("Registry configuration successfully exported to:\n" + sfd.FileName,
                            "Backup Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                } catch (Exception ex) {
                    MessageBox.Show("Failed to export registry backup: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnRestoreBackup.Click += (s, e) => {
                try {
                    OpenFileDialog ofd = new OpenFileDialog();
                    ofd.Filter = "Registry Files (*.reg)|*.reg";
                    ofd.Title = "Select Registry Backup File to Restore";
                    if (ofd.ShowDialog() == true) {
                        MessageBoxResult res = MessageBox.Show(
                            "Are you sure you want to restore settings from:\n" + ofd.FileName + "\n\nThis will overwrite current WDDM memory manager registry values.",
                            "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (res == MessageBoxResult.Yes) {
                            RestoreRegistryFile(ofd.FileName);
                        }
                    }
                } catch (Exception ex) {
                    MessageBox.Show("Failed to restore registry backup: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnApply.Click += (s, e) => {
                int frag = (int)sldFragBuffer.Value;
                int proc = (int)sldProcCap.Value;
                int shared = (int)sldSharedRam.Value;
                int hags = (cmbHags.SelectedIndex == 0) ? 1 : 2;
                ApplySettings(frag == STOCK_FRAG_MB ? (int?)null : frag,
                              proc == STOCK_PROC_MB ? (int?)null : proc,
                              shared == STOCK_SHARED_PCT ? (int?)null : shared,
                              hags);
            };

            btnRefresh.Click += (s, e) => {
                RefreshUIFromRegistry();
                txtActionNotice.Text = "Status refreshed from hardware.";
            };

            btnReboot.Click += (s, e) => {
                MessageBoxResult res = MessageBox.Show(
                    "Are you sure you want to restart your computer now to apply graphics changes?",
                    "Restart Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes) {
                    Process.Start("shutdown.exe", "/r /t 0");
                }
            };

            // Benchmark Execution (Automatic Hardware Pool on Selected GPU)
            btnRunVramTest.Click += (s, e) => {
                btnRunVramTest.IsEnabled = false;
                btnStopVramTest.IsEnabled = true;
                string gpuLabel = (selectedGpu != null) ? selectedGpu.Name : "GPU";
                txtTestLog.Text = string.Format("Starting automatic benchmark on {0}...\r\n", gpuLabel);
                pbVramTest.Value = 0;

                int targetIdx = (selectedGpu != null) ? selectedGpu.Index : -1;

                Thread t = new Thread(() => {
                    VramBenchmark.RunFullBenchmark(
                        targetIdx,
                        msg => Window.Dispatcher.Invoke(() => {
                            txtTestLog.AppendText(msg + "\r\n");
                            txtTestLog.ScrollToEnd();
                        }),
                        pct => Window.Dispatcher.Invoke(() => {
                            pbVramTest.Value = pct;
                        }),
                        () => Window.Dispatcher.Invoke(() => {
                            btnRunVramTest.IsEnabled = true;
                            btnStopVramTest.IsEnabled = false;
                            UpdateDashboardCards();
                        })
                    );
                });
                t.IsBackground = true;
                t.Start();
            };

            btnStopVramTest.Click += (s, e) => {
                VramBenchmark.CancelRequested = true;
                btnStopVramTest.IsEnabled = false;
                txtTestLog.AppendText("Halt signal sent. Releasing all resident buffers...\r\n");
            };

            // Keyboard Shortcuts (F5 Refresh, Ctrl+S Apply, Esc Stop)
            Window.KeyDown += (s, e) => {
                if (e.Key == System.Windows.Input.Key.F5) {
                    RefreshUIFromRegistry();
                    txtActionNotice.Text = "Status refreshed from hardware (F5).";
                } else if (e.Key == System.Windows.Input.Key.S && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) {
                    btnApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                } else if (e.Key == System.Windows.Input.Key.Escape) {
                    if (btnStopVramTest.IsEnabled) {
                        btnStopVramTest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }
            };

            CreateInitialBackup();
            SetTheme(true);
            RefreshUIFromRegistry();
        }

        private void CreateInitialBackup() {
            try {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string backupDir = Path.Combine(appDir, "Backups");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                string initBackup = Path.Combine(backupDir, "Initial_Registry_Backup.reg");
                if (!File.Exists(initBackup)) {
                    ExportRegistryFile(initBackup);
                }
            } catch { }
        }

        public static void ExportRegistryFile(string filePath) {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine();
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MemoryManager]");

            using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MM_PATH, false)) {
                if (mm != null) {
                    object frag = mm.GetValue("MaxVideoMemoryFragmentationBuffer");
                    if (frag != null) sb.AppendLine(string.Format("\"MaxVideoMemoryFragmentationBuffer\"=dword:{0:x8}", Convert.ToInt32(frag)));
                    else sb.AppendLine("\"MaxVideoMemoryFragmentationBuffer\"=-");

                    object proc = mm.GetValue("MaxProcessBudgetCapBuffer");
                    if (proc != null) sb.AppendLine(string.Format("\"MaxProcessBudgetCapBuffer\"=dword:{0:x8}", Convert.ToInt32(proc)));
                    else sb.AppendLine("\"MaxProcessBudgetCapBuffer\"=-");

                    object sysPart = mm.GetValue("SystemPartitionCommitLimitPercentage");
                    if (sysPart != null) sb.AppendLine(string.Format("\"SystemPartitionCommitLimitPercentage\"=dword:{0:x8}", Convert.ToInt32(sysPart)));
                    else sb.AppendLine("\"SystemPartitionCommitLimitPercentage\"=-");

                    object secPart = mm.GetValue("SecondaryPartitionCommitLimitPercentage");
                    if (secPart != null) sb.AppendLine(string.Format("\"SecondaryPartitionCommitLimitPercentage\"=dword:{0:x8}", Convert.ToInt32(secPart)));
                    else sb.AppendLine("\"SecondaryPartitionCommitLimitPercentage\"=-");
                } else {
                    sb.AppendLine("\"MaxVideoMemoryFragmentationBuffer\"=-");
                    sb.AppendLine("\"MaxProcessBudgetCapBuffer\"=-");
                    sb.AppendLine("\"SystemPartitionCommitLimitPercentage\"=-");
                    sb.AppendLine("\"SecondaryPartitionCommitLimitPercentage\"=-");
                }
            }

            sb.AppendLine();
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]");
            using (RegistryKey gd = Registry.LocalMachine.OpenSubKey(GD_PATH, false)) {
                if (gd != null) {
                    object hags = gd.GetValue("HwSchMode");
                    if (hags != null) sb.AppendLine(string.Format("\"HwSchMode\"=dword:{0:x8}", Convert.ToInt32(hags)));
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
        }

        private void RestoreRegistryFile(string filePath) {
            try {
                ProcessStartInfo psi = new ProcessStartInfo("reg.exe", string.Format("import \"{0}\"", filePath));
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                p.WaitForExit();
                RefreshUIFromRegistry();
                txtActionNotice.Text = string.Format("Restored successfully from {0}", Path.GetFileName(filePath));
                MessageBox.Show("Registry settings restored successfully from:\n" + filePath + "\n\nPlease restart your computer to apply.",
                    "Restore Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                MessageBox.Show("Failed to restore registry: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetBrushColor(string key, string hex) {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            SolidColorBrush b = Window.Resources.Contains(key) ? (Window.Resources[key] as SolidColorBrush) : null;
            if (b != null && !b.IsFrozen) {
                b.Color = c;
            } else {
                SolidColorBrush nb = new SolidColorBrush(c);
                Window.Resources[key] = nb;
            }
            if (Application.Current != null && Application.Current.Resources != null) {
                Application.Current.Resources[key] = Window.Resources[key];
            }
        }

        private void SetTheme(bool dark) {
            isDarkMode = dark;
            if (dark) {
                SetBrushColor("BgBrush", "#0B1120");
                SetBrushColor("CardBrush", "#1E293B");
                SetBrushColor("CardBorderBrush", "#334155");
                SetBrushColor("TextBrush", "#F8FAFC");
                SetBrushColor("SubTextBrush", "#94A3B8");
                SetBrushColor("HeaderBrush", "#FFFFFF");
                SetBrushColor("InputBgBrush", "#0F172A");
                SetBrushColor("InputFgBrush", "#F8FAFC");
                SetBrushColor("InputBorderBrush", "#475569");
                SetBrushColor("ConsoleBgBrush", "#020617");
                SetBrushColor("ConsoleFgBrush", "#38BDF8");
                SetBrushColor("CardTagDxgiBrush", "#38BDF8");
                SetBrushColor("CardTagWddmBrush", "#818CF8");
                SetBrushColor("CardTagSharedBrush", "#34D399");
                SetBrushColor("CardTagGpuBrush", "#FBBF24");
                SetBrushColor("SectionTitleBrush", "#38BDF8");
                SetBrushColor("AccentGreenBrush", "#4ADE80");
                SetBrushColor("SliderTrackBrush", "#334155");
                SetBrushColor("SliderThumbBrush", "#38BDF8");

                btnThemeToggle.Content = "☀️ Light Mode";
                btnThemeToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                btnThemeToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            } else {
                SetBrushColor("BgBrush", "#F8FAFC");
                SetBrushColor("CardBrush", "#FFFFFF");
                SetBrushColor("CardBorderBrush", "#E2E8F0");
                SetBrushColor("TextBrush", "#0F172A");
                SetBrushColor("SubTextBrush", "#475569");
                SetBrushColor("HeaderBrush", "#0F172A");
                SetBrushColor("InputBgBrush", "#F1F5F9");
                SetBrushColor("InputFgBrush", "#0F172A");
                SetBrushColor("InputBorderBrush", "#94A3B8");
                SetBrushColor("ConsoleBgBrush", "#0F172A");
                SetBrushColor("ConsoleFgBrush", "#38BDF8");
                SetBrushColor("CardTagDxgiBrush", "#0284C7");
                SetBrushColor("CardTagWddmBrush", "#4F46E5");
                SetBrushColor("CardTagSharedBrush", "#059669");
                SetBrushColor("CardTagGpuBrush", "#D97706");
                SetBrushColor("SectionTitleBrush", "#0284C7");
                SetBrushColor("AccentGreenBrush", "#16A34A");
                SetBrushColor("SliderTrackBrush", "#CBD5E1");
                SetBrushColor("SliderThumbBrush", "#0284C7");

                btnThemeToggle.Content = "🌙 Dark Mode";
                btnThemeToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                btnThemeToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            }

            UpdateDashboardCards();
        }

        private void UpdateDashboardCards() {
            if (selectedGpu != null) {
                liveBudgetMb = (selectedGpu.DedicatedBudgetMb > 0) ? selectedGpu.DedicatedBudgetMb : selectedGpu.DedicatedMb;
                liveSharedBudgetMb = (selectedGpu.SharedBudgetMb > 0) ? selectedGpu.SharedBudgetMb : selectedGpu.SharedMb;
                liveVramMb = selectedGpu.DedicatedMb;
            }

            if (liveBudgetMb > 0) {
                lblLiveVramBudget.Text = string.Format("{0:N0} MiB", liveBudgetMb);
                double gain = liveBudgetMb - (liveVramMb * 0.85);
                if (gain > 0) {
                    lblLiveVramGain.Text = string.Format("{0:F2} GiB Accessible", liveBudgetMb / 1024.0);
                    lblLiveVramGain.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                } else {
                    lblLiveVramGain.Text = "Hardware Baseline";
                    lblLiveVramGain.Foreground = (Brush)Window.Resources["SubTextBrush"];
                }
            } else if (liveVramMb > 0) {
                lblLiveVramBudget.Text = string.Format("{0:N0} MiB", liveVramMb);
                lblLiveVramGain.Text = "DirectX Hardware VRAM";
            }

            int frag = 512, proc = 256;
            using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MM_PATH, false)) {
                if (mm != null) {
                    object oFrag = mm.GetValue("MaxVideoMemoryFragmentationBuffer");
                    if (oFrag != null) frag = Convert.ToInt32(oFrag);
                    object oProc = mm.GetValue("MaxProcessBudgetCapBuffer");
                    if (oProc != null) proc = Convert.ToInt32(oProc);
                }
            }

            int totalRes = frag + proc;
            lblLiveWddmReserve.Text = string.Format("{0} MiB", totalRes);
            if (totalRes <= 160) {
                lblLiveWddmState.Text = string.Format("Optimized ({0}+{1})", frag, proc);
                lblLiveWddmState.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
            } else {
                lblLiveWddmState.Text = "Stock Windows (512+256)";
                lblLiveWddmState.Foreground = (Brush)Window.Resources["CardTagGpuBrush"];
            }

            int sharedPct = 50;
            using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MM_PATH, false)) {
                if (mm != null) {
                    object oShared = mm.GetValue("SystemPartitionCommitLimitPercentage");
                    if (oShared != null) sharedPct = Convert.ToInt32(oShared);
                }
            }
            lblLiveSharedRam.Text = string.Format("{0}%", sharedPct);
            double approxGb = Math.Round((sharedPct * totalRamGb) / 100.0, 1);
            lblLiveSharedGb.Text = string.Format("~{0:F1} GB on {1:F0}GB RAM", approxGb, totalRamGb);

            // Card 4: Total Combined Addressable Memory
            double combinedMb = liveBudgetMb + liveSharedBudgetMb;
            double totalPoolGiB = combinedMb / 1024.0;
            lblLiveTotalPool.Text = string.Format("Total Pool: ~{0:F1} GiB", totalPoolGiB);

            // Update Benchmark Targets
            if (lblBenchGpuName != null && selectedGpu != null) {
                lblBenchGpuName.Text = string.Format("Target GPU: {0}", selectedGpu.Name.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel ", ""));
            }
            double dedMb = (liveBudgetMb > 0 ? liveBudgetMb : liveVramMb);
            lblBenchDedicated.Text = string.Format("{0:N0} MiB ({1:F1} GiB)", dedMb, dedMb / 1024.0);
            lblBenchShared.Text = string.Format("{0:N0} MiB ({1:F1} GiB)", liveSharedBudgetMb, liveSharedBudgetMb / 1024.0);
            lblBenchCombined.Text = string.Format("{0:N0} MiB (~{1:F1} GiB)", combinedMb, totalPoolGiB);
        }

        private void RefreshUIFromRegistry() {
            UpdateDashboardCards();

            int frag = STOCK_FRAG_MB;
            int proc = STOCK_PROC_MB;
            int shared = STOCK_SHARED_PCT;
            using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MM_PATH, false)) {
                if (mm != null) {
                    object oFrag = mm.GetValue("MaxVideoMemoryFragmentationBuffer");
                    if (oFrag != null) frag = Convert.ToInt32(oFrag);
                    object oProc = mm.GetValue("MaxProcessBudgetCapBuffer");
                    if (oProc != null) proc = Convert.ToInt32(oProc);
                    object oShared = mm.GetValue("SystemPartitionCommitLimitPercentage");
                    if (oShared != null) shared = Convert.ToInt32(oShared);
                }
            }

            sldFragBuffer.Minimum = 32;
            sldFragBuffer.Maximum = STOCK_FRAG_MB;
            sldFragBuffer.Value = Math.Max(32, Math.Min(STOCK_FRAG_MB, frag));

            sldProcCap.Minimum = 32;
            sldProcCap.Maximum = STOCK_PROC_MB;
            sldProcCap.Value = Math.Max(32, Math.Min(STOCK_PROC_MB, proc));

            sldSharedRam.Minimum = 50;
            sldSharedRam.Maximum = 90;
            sldSharedRam.Value = Math.Max(50, Math.Min(90, shared));

            int hags = 2;
            using (RegistryKey gd = Registry.LocalMachine.OpenSubKey(GD_PATH, false)) {
                if (gd != null) {
                    object o = gd.GetValue("HwSchMode");
                    if (o != null) hags = Convert.ToInt32(o);
                }
            }
            cmbHags.SelectedIndex = (hags == 1) ? 0 : 1;
        }

        private void ApplySettings(int? frag, int? proc, int? shared, int? hags) {
            try {
                using (RegistryKey mm = Registry.LocalMachine.CreateSubKey(MM_PATH)) {
                    if (mm != null) {
                        if (frag == null) {
                            mm.DeleteValue("MaxVideoMemoryFragmentationBuffer", false);
                            mm.DeleteValue("MaxProcessBudgetCapBuffer", false);
                        } else {
                            mm.SetValue("MaxVideoMemoryFragmentationBuffer", frag.Value, RegistryValueKind.DWord);
                            mm.SetValue("MaxProcessBudgetCapBuffer", proc.Value, RegistryValueKind.DWord);
                        }

                        if (shared == null) {
                            mm.DeleteValue("SystemPartitionCommitLimitPercentage", false);
                            mm.DeleteValue("SecondaryPartitionCommitLimitPercentage", false);
                        } else {
                            mm.SetValue("SystemPartitionCommitLimitPercentage", shared.Value, RegistryValueKind.DWord);
                            mm.SetValue("SecondaryPartitionCommitLimitPercentage", shared.Value, RegistryValueKind.DWord);
                        }
                    }
                }

                if (hags != null) {
                    using (RegistryKey gd = Registry.LocalMachine.CreateSubKey(GD_PATH)) {
                        if (gd != null) {
                            gd.SetValue("HwSchMode", hags.Value, RegistryValueKind.DWord);
                        }
                    }
                }

                RefreshUIFromRegistry();
                txtActionNotice.Text = "Settings applied successfully! Restart required to take effect.";
            } catch (Exception ex) {
                MessageBox.Show("Failed to write settings to registry: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
