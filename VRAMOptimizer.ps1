Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# Ensure Administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    [System.Windows.MessageBox]::Show("Please run this tool as Administrator to modify Windows graphics registry settings.", "Administrator Required", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
    Exit
}

# Compile VRAM benchmark helper class
$csharpCode = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    public struct D3D11_SUBRESOURCE_DATA {
        public IntPtr pSysMem;
        public uint SysMemPitch;
        public uint SysMemSlicePitch;
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

    public static string GetGpuSummary() {
        List<GpuAdapterInfo> gpus = GetAllGpuAdapters();
        if (gpus.Count == 0) return "Generic GPU|0|0|0|0";
        GpuAdapterInfo best = gpus[0];
        foreach (var g in gpus) {
            if (g.DedicatedMb > best.DedicatedMb) best = g;
        }
        return string.Format("{0}|{1:F0}|{2:F0}|{3:F0}|{4:F0}", best.Name, best.DedicatedMb, best.DedicatedBudgetMb, best.SharedMb, best.SharedBudgetMb);
    }

    public static void RunFullBenchmark(int targetAdapterIndex, Action<string> logCallback, Action<int> progressCallback, Action onFinished) {
        CancelRequested = false;
        Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        IDXGIFactory1 factory;
        CreateDXGIFactory1(ref iid, out factory);

        IntPtr targetAdapterPtr = IntPtr.Zero;
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
            onFinished();
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

        ID3D11Device dev;
        int featLevel;
        IntPtr ctx;
        int hr = D3D11CreateDevice(targetAdapterPtr, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, out dev, out featLevel, out ctx);
        if (hr != 0) {
            Marshal.Release(targetAdapterPtr);
            logCallback(string.Format("Failed to initialize Direct3D 11 device (hr=0x{0:X8})", hr));
            onFinished();
            return;
        }

        uint chunkSize = 64 * 1024 * 1024; // 64 MiB chunks
        byte[] initialBytes = new byte[chunkSize];
        for (int i = 0; i < initialBytes.Length; i += 4096) initialBytes[i] = 0x5A; // touch pages to commit
        GCHandle pinnedHandle = GCHandle.Alloc(initialBytes, GCHandleType.Pinned);

        D3D11_SUBRESOURCE_DATA srd = new D3D11_SUBRESOURCE_DATA();
        srd.pSysMem = pinnedHandle.AddrOfPinnedObject();
        srd.SysMemPitch = chunkSize;
        srd.SysMemSlicePitch = chunkSize;

        // Phase 1 Descriptor: Local Dedicated VRAM
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

        List<IntPtr> buffers = new List<IntPtr>();
        ulong totalAllocated = 0;
        bool enteredShared = false;

        logCallback("Allocating GPU-resident memory blocks (64 MiB each)...");
        while (totalAllocated < totalMaxBytes) {
            if (CancelRequested) {
                logCallback("-> Allocation halted by user.");
                break;
            }

            bool isPhase1 = (totalAllocated < dedicatedBudget);

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

                int pct = (int)((totalAllocated * 100UL) / totalMaxBytes);
                if (pct > 100) pct = 100;
                progressCallback(pct);

                ulong curDedicated = Math.Min(totalAllocated, dedicatedBudget);
                ulong curShared = totalAllocated > dedicatedBudget ? (totalAllocated - dedicatedBudget) : 0;
                double curDedGiB = curDedicated / 1073741824.0;
                double curSharedGiB = curShared / 1073741824.0;
                double dedPct = (dedicatedBudget > 0) ? (curDedicated * 100.0 / dedicatedBudget) : 0;
                double shPct = (sharedBudget > 0) ? (curShared * 100.0 / sharedBudget) : 0;

                string phaseTag = isPhase1 ? "[PHASE 1 - DEDICATED VRAM]" : "[PHASE 2 - SHARED GPU RAM]";

                logCallback(string.Format("  -> {0} Committed: {1:F2} / {2:F2} GiB ({3}%) | Dedicated: {4:F2} GiB ({5:F0}%) | Shared GPU Memory: {6:F2} GiB ({7:F0}%)",
                    phaseTag, totalAllocated / 1073741824.0, totalMaxGiB, pct, curDedGiB, dedPct, curSharedGiB, shPct));
            }
        }

        progressCallback(100);

        logCallback("--------------------------------------------------");
        logCallback(string.Format("★ BENCHMARK FINISHED: Successfully allocated {0:F2} GiB ({1:N0} MiB)!", totalAllocated / 1073741824.0, totalAllocated / 1048576));
        logCallback(string.Format("Created {0} resident Direct3D 11 buffers across Dedicated VRAM + Shared GPU Memory.", buffers.Count));
        logCallback("--------------------------------------------------");

        logCallback("Releasing all allocated buffers...");
        if (pinnedHandle.IsAllocated) pinnedHandle.Free();
        foreach (var b in buffers) {
            Marshal.Release(b);
        }
        if (ctx != IntPtr.Zero) Marshal.Release(ctx);
        Marshal.Release(targetAdapterPtr);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        logCallback("✔ All buffers freed. All memory safely returned to Windows.");
        onFinished();
    }
}
"@

Add-Type -TypeDefinition $csharpCode

$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Windows WDDM &amp; VRAM Optimizer" Height="850" Width="910"
        WindowStartupLocation="CenterScreen" ResizeMode="CanMinimize"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource TextBrush}" FontFamily="Segoe UI">
    
    <Window.Resources>
        <SolidColorBrush x:Key="BgBrush" Color="#0B1120"/>
        <SolidColorBrush x:Key="CardBrush" Color="#1E293B"/>
        <SolidColorBrush x:Key="CardBorderBrush" Color="#334155"/>
        <SolidColorBrush x:Key="TextBrush" Color="#F8FAFC"/>
        <SolidColorBrush x:Key="SubTextBrush" Color="#94A3B8"/>
        <SolidColorBrush x:Key="HeaderBrush" Color="#FFFFFF"/>
        <SolidColorBrush x:Key="InputBgBrush" Color="#0F172A"/>
        <SolidColorBrush x:Key="InputFgBrush" Color="#F8FAFC"/>
        <SolidColorBrush x:Key="InputBorderBrush" Color="#475569"/>
        <SolidColorBrush x:Key="ConsoleBgBrush" Color="#020617"/>
        <SolidColorBrush x:Key="ConsoleFgBrush" Color="#38BDF8"/>
        <SolidColorBrush x:Key="CardTagDxgiBrush" Color="#38BDF8"/>
        <SolidColorBrush x:Key="CardTagWddmBrush" Color="#818CF8"/>
        <SolidColorBrush x:Key="CardTagSharedBrush" Color="#34D399"/>
        <SolidColorBrush x:Key="CardTagGpuBrush" Color="#FBBF24"/>
        <SolidColorBrush x:Key="SectionTitleBrush" Color="#38BDF8"/>
        <SolidColorBrush x:Key="AccentGreenBrush" Color="#4ADE80"/>
        <SolidColorBrush x:Key="SliderTrackBrush" Color="#334155"/>
        <SolidColorBrush x:Key="SliderThumbBrush" Color="#38BDF8"/>

        <!-- Button Style -->
        <Style TargetType="Button">
            <Setter Property="Padding" Value="14,8"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="border" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Opacity" Value="0.88"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Opacity" Value="0.75"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.45"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Thumb Style -->
        <Style x:Key="SliderThumbStyle" TargetType="Thumb">
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Thumb">
                        <Ellipse Width="18" Height="18" Fill="{DynamicResource SliderThumbBrush}" Stroke="#FFFFFF" StrokeThickness="2" Cursor="Hand"/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Template Style -->
        <Style TargetType="Slider">
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid VerticalAlignment="Center">
                            <Border Height="6" Background="{DynamicResource SliderTrackBrush}" CornerRadius="3" VerticalAlignment="Center"/>
                            <Track x:Name="PART_Track">
                                <Track.Thumb>
                                    <Thumb Style="{StaticResource SliderThumbStyle}"/>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBoxItem Style -->
        <Style TargetType="ComboBoxItem">
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="Padding" Value="10,6"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource InputFgBrush}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBoxItem">
                        <Border x:Name="ItemBorder" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}" Margin="1">
                            <ContentPresenter x:Name="ItemContent" 
                                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" 
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}" 
                                              TextElement.Foreground="{TemplateBinding Foreground}" 
                                              TextElement.FontSize="12"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsHighlighted" Value="True">
                                <Setter TargetName="ItemBorder" Property="Background" Value="#2563EB"/>
                                <Setter Property="Foreground" Value="#FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="ItemBorder" Property="Background" Value="#1D4ED8"/>
                                <Setter Property="Foreground" Value="#FFFFFF"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBox ToggleButton Template -->
        <ControlTemplate x:Key="ComboBoxToggleButtonTemplate" TargetType="ToggleButton">
            <Border x:Name="templateRoot" 
                    Background="{DynamicResource InputBgBrush}" 
                    BorderBrush="{DynamicResource InputBorderBrush}" 
                    BorderThickness="1" 
                    CornerRadius="6">
                <Border x:Name="splitBorder" Width="30" HorizontalAlignment="Right">
                    <Path x:Name="arrow" 
                          Data="M 0 0 L 4.5 4.5 L 9 0 Z" 
                          Fill="{DynamicResource InputFgBrush}" 
                          HorizontalAlignment="Center" 
                          VerticalAlignment="Center"/>
                </Border>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="templateRoot" Property="BorderBrush" Value="#38BDF8"/>
                </Trigger>
                <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="templateRoot" Property="BorderBrush" Value="#2563EB"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>

        <!-- Custom ComboBox Style -->
        <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="{DynamicResource InputFgBrush}"/>
            <Setter Property="Background" Value="{DynamicResource InputBgBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="FontSize" Value="12.5"/>
            <Setter Property="FontWeight" Value="Medium"/>
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBox">
                        <Grid x:Name="templateRoot">
                            <ToggleButton x:Name="toggleButton" 
                                          Template="{StaticResource ComboBoxToggleButtonTemplate}" 
                                          Focusable="False" 
                                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" 
                                          ClickMode="Press"/>
                            <ContentPresenter x:Name="contentPresenter" 
                                              Content="{TemplateBinding SelectionBoxItem}" 
                                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" 
                                              ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" 
                                              Margin="10,0,32,0" 
                                              HorizontalAlignment="Left" 
                                              VerticalAlignment="Center" 
                                              IsHitTestVisible="False"
                                              TextElement.Foreground="{DynamicResource InputFgBrush}"
                                              TextElement.FontSize="12"
                                              TextElement.FontWeight="SemiBold"/>
                            <Popup x:Name="PART_Popup" 
                                   AllowsTransparency="True" 
                                   IsOpen="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}" 
                                   Placement="Bottom" 
                                   PopupAnimation="Fade">
                                <Border x:Name="dropDownBorder" 
                                        Background="{DynamicResource CardBrush}" 
                                        BorderBrush="{DynamicResource CardBorderBrush}" 
                                        BorderThickness="1" 
                                        CornerRadius="6" 
                                        Margin="0,3,0,0" 
                                        Padding="4" 
                                        MinWidth="{Binding ActualWidth, ElementName=templateRoot}" 
                                        MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                    <ScrollViewer x:Name="DropDownScrollViewer" CanContentScroll="True">
                                        <ItemsPresenter x:Name="ItemsPresenter" KeyboardNavigation.DirectionalNavigation="Contained"/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Top Header & Theme Switcher -->
        <Grid Grid.Row="0" Margin="0,0,0,14">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="Windows WDDM &amp; VRAM Optimizer" FontSize="23" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}"/>
                <TextBlock Text="Dynamic GPU-tailored VRAM optimization &amp; full PCIe Shared System RAM allocation" FontSize="13" Foreground="{DynamicResource SubTextBrush}" Margin="0,3,0,0"/>
            </StackPanel>
            <Button x:Name="btnThemeToggle" Grid.Column="1" Content="☀️ Light Mode" Background="#334155" Foreground="#F8FAFC" VerticalAlignment="Center" Padding="12,7" ToolTip="Toggle between Dark Mode and Light Mode"/>
        </Grid>

        <!-- 4 Live Status Cards (Dynamic Hardware Pool) -->
        <Grid Grid.Row="1" Margin="0,0,0,14">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="1.25*"/>
            </Grid.ColumnDefinitions>

            <!-- Card 1: Dedicated Budget -->
            <Border Grid.Column="0" Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="12" Margin="0,0,5,0">
                <StackPanel>
                    <TextBlock Text="DXGI DEDICATED BUDGET" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource CardTagDxgiBrush}"/>
                    <TextBlock x:Name="lblLiveVramBudget" Text="5,760 MiB" FontSize="17" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}"/>
                    <TextBlock x:Name="lblLiveVramGain" Text="+607 MiB Unlocked" FontSize="11" Foreground="{DynamicResource AccentGreenBrush}" Margin="0,2,0,0"/>
                </StackPanel>
            </Border>

            <!-- Card 2: WDDM Reserve -->
            <Border Grid.Column="1" Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="12" Margin="4,0,4,0">
                <StackPanel>
                    <TextBlock Text="WDDM KERNEL RESERVE" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource CardTagWddmBrush}"/>
                    <TextBlock x:Name="lblLiveWddmReserve" Text="160 MiB" FontSize="17" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}"/>
                    <TextBlock x:Name="lblLiveWddmState" Text="Optimized (128+32)" FontSize="11" Foreground="{DynamicResource CardTagWddmBrush}" Margin="0,2,0,0"/>
                </StackPanel>
            </Border>

            <!-- Card 3: Shared RAM Cap -->
            <Border Grid.Column="2" Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="12" Margin="4,0,4,0">
                <StackPanel>
                    <TextBlock Text="SHARED SYSTEM RAM CAP" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource CardTagSharedBrush}"/>
                    <TextBlock x:Name="lblLiveSharedRam" Text="75%" FontSize="17" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}"/>
                    <TextBlock x:Name="lblLiveSharedGb" Text="~24.0 GB Limit" FontSize="11" Foreground="{DynamicResource CardTagSharedBrush}" Margin="0,2,0,0"/>
                </StackPanel>
            </Border>

            <!-- Card 4: Active GPU Adapter & Combined Max Pool -->
            <Border Grid.Column="3" Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="10,8" Margin="5,0,0,0">
                <StackPanel>
                    <Grid Margin="0,0,0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="SELECT GPU ADAPTER" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource CardTagGpuBrush}"/>
                        <TextBlock Grid.Column="1" Text="Multi-GPU" FontSize="9" FontWeight="SemiBold" Foreground="{DynamicResource SubTextBrush}"/>
                    </Grid>
                    <ComboBox x:Name="cmbGpuSelector" Height="28" Margin="0,2,0,3" ToolTip="Select which GPU adapter to monitor, optimize, and benchmark"/>
                    <TextBlock x:Name="lblLiveTotalPool" Text="Total Pool: ~30.8 GiB" FontSize="10.5" Foreground="{DynamicResource AccentGreenBrush}" TextTrimming="CharacterEllipsis"/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Main Body: Presets, Dynamic Sliders, Full Memory Benchmark -->
        <ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="0,0,6,0">

                <!-- One-Click Presets -->
                <Border Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="14" Margin="0,0,0,12">
                    <StackPanel>
                        <TextBlock Text="ONE-CLICK OPTIMIZATION PRESETS" FontWeight="Bold" FontSize="12" Foreground="{DynamicResource SubTextBrush}" Margin="0,0,0,10"/>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name="btnPresetRecommended" Grid.Column="0" Content="★ Recommended (Optimal)" Background="#2563EB" Foreground="White" Margin="0,0,5,0" ToolTip="Applies 128MB frag buffer, 32MB process cap, and 75% Shared RAM (recovers +608MB VRAM)."/>
                            <Button x:Name="btnPresetAggressive" Grid.Column="1" Content="⚡ Aggressive (AI / LLM)" Background="#7C3AED" Foreground="White" Margin="5,0,5,0" ToolTip="Applies 32MB minimum hardware floor and 80% Shared RAM for maximum headroom."/>
                            <Button x:Name="btnPresetDefault" Grid.Column="2" Content="↺ Windows Stock Defaults" Background="#475569" Foreground="#F8FAFC" Margin="5,0,0,0" ToolTip="Removes custom overrides and restores stock Windows driver defaults."/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Dynamic Fine-Grained Sliders Card -->
                <Border Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="16" Margin="0,0,0,12">
                    <StackPanel>
                        <TextBlock Text="FINE-GRAINED SLIDERS (DYNAMIC GPU VRAM SIZING)" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource SectionTitleBrush}" Margin="0,0,0,14"/>
                        
                        <!-- Slider 1: Fragmentation Buffer -->
                        <Grid Margin="0,0,0,14">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row="0" Margin="0,0,0,6">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="Max Video Memory Fragmentation Buffer" FontWeight="SemiBold" FontSize="13" Foreground="{DynamicResource TextBrush}"/>
                                    <TextBlock Text="Kernel page-table &amp; defragmentation reserve. Lower values unlock dedicated VRAM." FontSize="11" Foreground="{DynamicResource SubTextBrush}"/>
                                </StackPanel>
                                <Border Grid.Column="1" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,4" VerticalAlignment="Center">
                                    <TextBlock x:Name="lblFragValue" Text="512 MiB (Stock Default)" FontWeight="Bold" FontSize="12" Foreground="{DynamicResource SubTextBrush}"/>
                                </Border>
                            </Grid>
                            <Slider x:Name="sldFragBuffer" Grid.Row="1" Height="26" Minimum="32" Maximum="512" Value="512" TickFrequency="32" IsSnapToTickEnabled="True"/>
                        </Grid>

                        <!-- Slider 2: Process Budget Cap -->
                        <Grid Margin="0,0,0,14">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row="0" Margin="0,0,0,6">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="Max Process Budget Cap Buffer" FontWeight="SemiBold" FontSize="13" Foreground="{DynamicResource TextBrush}"/>
                                    <TextBlock Text="Foreground application throttle cap for desktop/DWM. Lower values unlock dedicated VRAM." FontSize="11" Foreground="{DynamicResource SubTextBrush}"/>
                                </StackPanel>
                                <Border Grid.Column="1" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,4" VerticalAlignment="Center">
                                    <TextBlock x:Name="lblProcValue" Text="256 MiB (Stock Default)" FontWeight="Bold" FontSize="12" Foreground="{DynamicResource SubTextBrush}"/>
                                </Border>
                            </Grid>
                            <Slider x:Name="sldProcCap" Grid.Row="1" Height="26" Minimum="32" Maximum="256" Value="256" TickFrequency="32" IsSnapToTickEnabled="True"/>
                        </Grid>

                        <!-- Slider 3: Shared RAM Commit Limit -->
                        <Grid Margin="0,0,0,14">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row="0" Margin="0,0,0,6">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="Shared GPU Memory Commit Limit (System RAM)" FontWeight="SemiBold" FontSize="13" Foreground="{DynamicResource TextBrush}"/>
                                    <TextBlock Text="Maximum host physical RAM mapped to GPU over PCIe bus. Dynamically computed from system RAM." FontSize="11" Foreground="{DynamicResource SubTextBrush}"/>
                                </StackPanel>
                                <Border Grid.Column="1" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,4" VerticalAlignment="Center">
                                    <TextBlock x:Name="lblSharedValue" Text="50% (Stock Default)" FontWeight="Bold" FontSize="12" Foreground="{DynamicResource SubTextBrush}"/>
                                </Border>
                            </Grid>
                            <Slider x:Name="sldSharedRam" Grid.Row="1" Height="26" Minimum="50" Maximum="90" Value="50" TickFrequency="5" IsSnapToTickEnabled="True"/>
                        </Grid>

                        <!-- HAGS (Clean Toggle, Wide Column to prevent truncation) -->
                        <Grid Margin="0,2,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="310"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0">
                                <TextBlock Text="Hardware-Accelerated GPU Scheduling (HAGS)" FontWeight="SemiBold" FontSize="13" Foreground="{DynamicResource TextBrush}"/>
                                <TextBlock Text="OFF saves 40-100MB VRAM for CUDA/LLMs; ON required for DLSS Frame Gen." FontSize="11" Foreground="{DynamicResource SubTextBrush}"/>
                            </StackPanel>
                            <ComboBox x:Name="cmbHags" Grid.Column="1" Height="32" VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Live VRAM + Shared RAM Allocation Benchmark (Automatic Sizing) -->
                <Border Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="16" Margin="0,0,0,10">
                    <StackPanel>
                        <Grid Margin="0,0,0,3">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="LIVE VRAM + SHARED RAM ALLOCATION BENCHMARK" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource AccentGreenBrush}"/>
                            <TextBlock x:Name="lblBenchGpuName" Grid.Column="1" Text="GPU: RTX 4050" FontSize="11.5" FontWeight="SemiBold" Foreground="{DynamicResource CardTagGpuBrush}"/>
                        </Grid>
                        <TextBlock Text="Two-phase hardware allocator: Phase 1 saturates Dedicated VRAM; Phase 2 allocates PCIe Staging buffers into Shared GPU Memory (reflected 1:1 in Windows Task Manager). Zero manual sizing required." FontSize="11" Foreground="{DynamicResource SubTextBrush}" Margin="0,0,0,12"/>

                        <!-- Automatic Target Metrics -->
                        <Grid Margin="0,0,0,12">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="1.2*"/>
                            </Grid.ColumnDefinitions>

                            <Border Grid.Column="0" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,5,0">
                                <StackPanel>
                                    <TextBlock Text="DEDICATED VRAM TARGET" FontSize="9.5" FontWeight="Bold" Foreground="{DynamicResource CardTagDxgiBrush}"/>
                                    <TextBlock x:Name="lblBenchDedicated" Text="5,760 MiB (5.6 GiB)" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}" Margin="0,2,0,0"/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column="1" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="4,0,4,0">
                                <StackPanel>
                                    <TextBlock Text="SHARED RAM TARGET" FontSize="9.5" FontWeight="Bold" Foreground="{DynamicResource CardTagSharedBrush}"/>
                                    <TextBlock x:Name="lblBenchShared" Text="25,052 MiB (24.5 GiB)" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource HeaderBrush}" Margin="0,2,0,0"/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column="2" Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="5,0,0,0">
                                <StackPanel>
                                    <TextBlock Text="MAX COMBINED POOL" FontSize="9.5" FontWeight="Bold" Foreground="{DynamicResource AccentGreenBrush}"/>
                                    <TextBlock x:Name="lblBenchCombined" Text="30,812 MiB (~30.1 GiB)" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource AccentGreenBrush}" Margin="0,2,0,0"/>
                                </StackPanel>
                            </Border>
                        </Grid>

                        <!-- Action Buttons -->
                        <Grid Margin="0,0,0,10">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name="btnRunVramTest" Grid.Column="0" Content="▶ Run Full Memory Benchmark (Dedicated + Shared Pool)" Background="#16A34A" Foreground="White" Padding="16,8" Margin="0,0,8,0"/>
                            <Button x:Name="btnStopVramTest" Grid.Column="1" Content="■ Stop" Background="#DC2626" Foreground="White" Padding="16,8" IsEnabled="False"/>
                        </Grid>

                        <ProgressBar x:Name="pbVramTest" Height="8" Background="{DynamicResource InputBgBrush}" Foreground="#16A34A" Value="0" Margin="0,0,0,8"/>

                        <Border Background="{DynamicResource ConsoleBgBrush}" CornerRadius="6" Padding="10">
                            <TextBox x:Name="txtTestLog" Text="Ready to test. Maximum capacity is automatically set to your full combined hardware pool. Click 'Run Full Memory Benchmark' to begin." FontFamily="Consolas" FontSize="11.5" Foreground="{DynamicResource ConsoleFgBrush}" Background="Transparent" BorderThickness="0" IsReadOnly="True" TextWrapping="Wrap" MaxHeight="135" VerticalScrollBarVisibility="Auto"/>
                        </Border>
                    </StackPanel>
                </Border>

            </StackPanel>
        </ScrollViewer>

        <!-- Bottom Action Bar -->
        <Border Grid.Row="3" Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource CardBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="12" Margin="0,12,0,0">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Button x:Name="btnRefresh" Grid.Column="0" Content="Refresh Live Status" Background="#475569" Foreground="White" Margin="0,0,8,0"/>
                <TextBlock x:Name="txtActionNotice" Grid.Column="1" VerticalAlignment="Center" Margin="10,0" Foreground="{DynamicResource AccentGreenBrush}" FontWeight="SemiBold" Text=""/>
                <Button x:Name="btnApply" Grid.Column="2" Content="Apply Custom Settings" Background="#2563EB" Foreground="White" Margin="0,0,8,0"/>
                <Button x:Name="btnReboot" Grid.Column="3" Content="Restart Windows Now" Background="#DC2626" Foreground="White"/>
            </Grid>
        </Border>
    </Grid>
</Window>
"@

$reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($xaml))
$window = [System.Windows.Markup.XamlReader]::Load($reader)

# Element References
$btnThemeToggle       = $window.FindName("btnThemeToggle")
$lblLiveVramBudget    = $window.FindName("lblLiveVramBudget")
$lblLiveVramGain      = $window.FindName("lblLiveVramGain")
$lblLiveWddmReserve   = $window.FindName("lblLiveWddmReserve")
$lblLiveWddmState     = $window.FindName("lblLiveWddmState")
$lblLiveSharedRam     = $window.FindName("lblLiveSharedRam")
$lblLiveSharedGb      = $window.FindName("lblLiveSharedGb")
$cmbGpuSelector       = $window.FindName("cmbGpuSelector")
$lblLiveTotalPool     = $window.FindName("lblLiveTotalPool")

$btnPresetRecommended = $window.FindName("btnPresetRecommended")
$btnPresetAggressive  = $window.FindName("btnPresetAggressive")
$btnPresetDefault     = $window.FindName("btnPresetDefault")

$lblFragValue         = $window.FindName("lblFragValue")
$sldFragBuffer        = $window.FindName("sldFragBuffer")
$lblProcValue         = $window.FindName("lblProcValue")
$sldProcCap           = $window.FindName("sldProcCap")
$lblSharedValue       = $window.FindName("lblSharedValue")
$sldSharedRam         = $window.FindName("sldSharedRam")
$cmbHags              = $window.FindName("cmbHags")
$cmbHags.Items.Add("Disabled (Saves 40-100MB / HwSchMode=1)")
$cmbHags.Items.Add("Enabled (Required for Frame Gen / HwSchMode=2)")
$cmbHags.SelectedIndex = 1

$lblBenchGpuName      = $window.FindName("lblBenchGpuName")
$lblBenchDedicated    = $window.FindName("lblBenchDedicated")
$lblBenchShared       = $window.FindName("lblBenchShared")
$lblBenchCombined     = $window.FindName("lblBenchCombined")
$btnRunVramTest       = $window.FindName("btnRunVramTest")
$btnStopVramTest      = $window.FindName("btnStopVramTest")
$pbVramTest           = $window.FindName("pbVramTest")
$txtTestLog           = $window.FindName("txtTestLog")

$btnRefresh           = $window.FindName("btnRefresh")
$btnApply             = $window.FindName("btnApply")
$btnReboot            = $window.FindName("btnReboot")
$txtActionNotice      = $window.FindName("txtActionNotice")

$mmPath = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MemoryManager"
$gdPath = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"

# Theme State & Multi-GPU State
$script:isDarkMode = $true
$script:selectedGpu = $null
$script:liveBudgetMb = 0
$script:liveSharedBudgetMb = 0
$script:liveVramMb = 0
$script:totalRamGb = [VramBenchmark]::GetTotalSystemRamGb()
$STOCK_FRAG_MB = 512
$STOCK_PROC_MB = 256
$STOCK_SHARED_PCT = 50

$detectedGpus = [VramBenchmark]::GetAllGpuAdapters()
$cmbGpuSelector.ItemsSource = $detectedGpus

$defaultIndex = 0
$maxDed = -1
for ($i = 0; $i -lt $detectedGpus.Count; $i++) {
    if ($detectedGpus[$i].DedicatedMb -gt $maxDed) {
        $maxDed = $detectedGpus[$i].DedicatedMb
        $defaultIndex = $i
    }
}
if ($detectedGpus.Count -gt 0) {
    $cmbGpuSelector.SelectedIndex = $defaultIndex
    $script:selectedGpu = $detectedGpus[$defaultIndex]
    $script:liveBudgetMb = if ($script:selectedGpu.DedicatedBudgetMb -gt 0) { $script:selectedGpu.DedicatedBudgetMb } else { $script:selectedGpu.DedicatedMb }
    $script:liveVramMb = $script:selectedGpu.DedicatedMb
    $script:liveSharedBudgetMb = if ($script:selectedGpu.SharedBudgetMb -gt 0) { $script:selectedGpu.SharedBudgetMb } else { $script:selectedGpu.SharedMb }
}

$cmbGpuSelector.Add_SelectionChanged({
    $info = $cmbGpuSelector.SelectedItem
    if ($null -ne $info) {
        $script:selectedGpu = $info
        $script:liveBudgetMb = if ($info.DedicatedBudgetMb -gt 0) { $info.DedicatedBudgetMb } else { $info.DedicatedMb }
        $script:liveVramMb = $info.DedicatedMb
        $script:liveSharedBudgetMb = if ($info.SharedBudgetMb -gt 0) { $info.SharedBudgetMb } else { $info.SharedMb }

        Update-DashboardCards
    }
})

function Set-BrushColor ($key, $hex) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    if ($window.Resources.Contains($key) -and ($window.Resources[$key] -is [System.Windows.Media.SolidColorBrush]) -and (-not $window.Resources[$key].IsFrozen)) {
        $window.Resources[$key].Color = $c
    } else {
        $window.Resources[$key] = [System.Windows.Media.SolidColorBrush]::new($c)
    }
    if ($null -ne [System.Windows.Application]::Current -and $null -ne [System.Windows.Application]::Current.Resources) {
        [System.Windows.Application]::Current.Resources[$key] = $window.Resources[$key]
    }
}

function Set-Theme ($dark) {
    $script:isDarkMode = $dark
    if ($dark) {
        Set-BrushColor "BgBrush" "#0B1120"
        Set-BrushColor "CardBrush" "#1E293B"
        Set-BrushColor "CardBorderBrush" "#334155"
        Set-BrushColor "TextBrush" "#F8FAFC"
        Set-BrushColor "SubTextBrush" "#94A3B8"
        Set-BrushColor "HeaderBrush" "#FFFFFF"
        Set-BrushColor "InputBgBrush" "#0F172A"
        Set-BrushColor "InputFgBrush" "#F8FAFC"
        Set-BrushColor "InputBorderBrush" "#475569"
        Set-BrushColor "ConsoleBgBrush" "#020617"
        Set-BrushColor "ConsoleFgBrush" "#38BDF8"
        Set-BrushColor "CardTagDxgiBrush" "#38BDF8"
        Set-BrushColor "CardTagWddmBrush" "#818CF8"
        Set-BrushColor "CardTagSharedBrush" "#34D399"
        Set-BrushColor "CardTagGpuBrush" "#FBBF24"
        Set-BrushColor "SectionTitleBrush" "#38BDF8"
        Set-BrushColor "AccentGreenBrush" "#4ADE80"
        Set-BrushColor "SliderTrackBrush" "#334155"
        Set-BrushColor "SliderThumbBrush" "#38BDF8"

        $btnThemeToggle.Content = "☀️ Light Mode"
        $btnThemeToggle.Background = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#334155"))
        $btnThemeToggle.Foreground = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#F8FAFC"))
    } else {
        Set-BrushColor "BgBrush" "#F8FAFC"
        Set-BrushColor "CardBrush" "#FFFFFF"
        Set-BrushColor "CardBorderBrush" "#E2E8F0"
        Set-BrushColor "TextBrush" "#0F172A"
        Set-BrushColor "SubTextBrush" "#475569"
        Set-BrushColor "HeaderBrush" "#0F172A"
        Set-BrushColor "InputBgBrush" "#F1F5F9"
        Set-BrushColor "InputFgBrush" "#0F172A"
        Set-BrushColor "InputBorderBrush" "#94A3B8"
        Set-BrushColor "ConsoleBgBrush" "#0F172A"
        Set-BrushColor "ConsoleFgBrush" "#38BDF8"
        Set-BrushColor "CardTagDxgiBrush" "#0284C7"
        Set-BrushColor "CardTagWddmBrush" "#4F46E5"
        Set-BrushColor "CardTagSharedBrush" "#059669"
        Set-BrushColor "CardTagGpuBrush" "#D97706"
        Set-BrushColor "SectionTitleBrush" "#0284C7"
        Set-BrushColor "AccentGreenBrush" "#16A34A"
        Set-BrushColor "SliderTrackBrush" "#CBD5E1"
        Set-BrushColor "SliderThumbBrush" "#0284C7"

        $btnThemeToggle.Content = "🌙 Dark Mode"
        $btnThemeToggle.Background = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#E2E8F0"))
        $btnThemeToggle.Foreground = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#0F172A"))
    }

    Update-DashboardCards
}

$btnThemeToggle.Add_Click({
    Set-Theme (-not $script:isDarkMode)
})

function Update-DashboardCards {
    if ($null -ne $script:selectedGpu) {
        $script:liveBudgetMb = if ($script:selectedGpu.DedicatedBudgetMb -gt 0) { $script:selectedGpu.DedicatedBudgetMb } else { $script:selectedGpu.DedicatedMb }
        $script:liveSharedBudgetMb = if ($script:selectedGpu.SharedBudgetMb -gt 0) { $script:selectedGpu.SharedBudgetMb } else { $script:selectedGpu.SharedMb }
        $script:liveVramMb = $script:selectedGpu.DedicatedMb
    }

    $mm = Get-ItemProperty $mmPath -ErrorAction SilentlyContinue
    $gd = Get-ItemProperty $gdPath -ErrorAction SilentlyContinue

    # Update Dedicated Budget Card
    if ($script:liveBudgetMb -gt 0) {
        $lblLiveVramBudget.Text = "$([int]$script:liveBudgetMb) MiB"
        $gain = $script:liveBudgetMb - 5153
        if ($gain -gt 0) {
            $lblLiveVramGain.Text = "+$([int]$gain) MiB Unlocked"
            $lblLiveVramGain.Foreground = $window.Resources["AccentGreenBrush"]
        } else {
            $lblLiveVramGain.Text = "Windows Stock Baseline"
            $lblLiveVramGain.Foreground = $window.Resources["SubTextBrush"]
        }
    } elseif ($script:liveVramMb -gt 0) {
        $lblLiveVramBudget.Text = "$([int]$script:liveVramMb) MiB"
        $lblLiveVramGain.Text = "DirectX Hardware VRAM"
    }

    # Update WDDM Reserve Card
    $frag = if ($mm.MaxVideoMemoryFragmentationBuffer) { [int]$mm.MaxVideoMemoryFragmentationBuffer } else { 512 }
    $proc = if ($mm.MaxProcessBudgetCapBuffer) { [int]$mm.MaxProcessBudgetCapBuffer } else { 256 }
    $totalRes = $frag + $proc
    $lblLiveWddmReserve.Text = "$totalRes MiB"
    if ($totalRes -le 160) {
        $lblLiveWddmState.Text = "Optimized ($frag+$proc)"
        $lblLiveWddmState.Foreground = $window.Resources["AccentGreenBrush"]
    } else {
        $lblLiveWddmState.Text = "Stock Windows (512+256)"
        $lblLiveWddmState.Foreground = $window.Resources["CardTagGpuBrush"]
    }

    # Update Shared RAM Card
    $sharedPct = if ($mm.SystemPartitionCommitLimitPercentage) { [int]$mm.SystemPartitionCommitLimitPercentage } else { 50 }
    $lblLiveSharedRam.Text = "$sharedPct%"
    $approxGb = [Math]::Round(($sharedPct * $script:totalRamGb) / 100.0, 1)
    $lblLiveSharedGb.Text = "~$approxGb GB Limit"

    # Update Card 4: Total Pool
    $combinedMb = ($script:liveBudgetMb + $script:liveSharedBudgetMb)
    $totalPoolGiB = $combinedMb / 1024.0
    $lblLiveTotalPool.Text = "Total Pool: ~$([Math]::Round($totalPoolGiB, 1)) GiB"

    # Update Benchmark Cards
    $lblBenchGpuName.Text = if ($null -ne $script:selectedGpu) {
        "Target GPU: " + $script:selectedGpu.Name.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel ", "")
    } else { "Target GPU: Hardware Adapter" }

    $dedMb = if ($script:liveBudgetMb -gt 0) { $script:liveBudgetMb } else { $script:liveVramMb }
    $lblBenchDedicated.Text = "$([int]$dedMb) MiB ($([Math]::Round($dedMb/1024.0, 1)) GiB)"
    $lblBenchShared.Text = "$([int]$script:liveSharedBudgetMb) MiB ($([Math]::Round($script:liveSharedBudgetMb/1024.0, 1)) GiB)"
    $lblBenchCombined.Text = "$([int]$combinedMb) MiB (~$([Math]::Round($totalPoolGiB, 1)) GiB)"
}

# Slider Handlers
$sldFragBuffer.Add_ValueChanged({
    $val = [int]$sldFragBuffer.Value
    $saved = $STOCK_FRAG_MB - $val
    if ($saved -gt 0) {
        $lblFragValue.Text = "$val MiB (+$saved MiB Unlocked)"
        $lblFragValue.Foreground = $window.Resources["AccentGreenBrush"]
    } else {
        $lblFragValue.Text = "$val MiB (Stock Default)"
        $lblFragValue.Foreground = $window.Resources["SubTextBrush"]
    }
})

$sldProcCap.Add_ValueChanged({
    $val = [int]$sldProcCap.Value
    $saved = $STOCK_PROC_MB - $val
    if ($saved -gt 0) {
        $lblProcValue.Text = "$val MiB (+$saved MiB Unlocked)"
        $lblProcValue.Foreground = $window.Resources["AccentGreenBrush"]
    } else {
        $lblProcValue.Text = "$val MiB (Stock Default)"
        $lblProcValue.Foreground = $window.Resources["SubTextBrush"]
    }
})

$sldSharedRam.Add_ValueChanged({
    $val = [int]$sldSharedRam.Value
    $gb = [Math]::Round(($val * $script:totalRamGb) / 100.0, 1)
    if ($val -gt $STOCK_SHARED_PCT) {
        $extra = $val - $STOCK_SHARED_PCT
        $lblSharedValue.Text = "$val% (~$gb GB on $([int]$script:totalRamGb)GB RAM / +$extra% Shared)"
        $lblSharedValue.Foreground = $window.Resources["AccentGreenBrush"]
    } else {
        $lblSharedValue.Text = "$val% (Stock Default / ~$gb GB on $([int]$script:totalRamGb)GB RAM)"
        $lblSharedValue.Foreground = $window.Resources["SubTextBrush"]
    }
})

function Refresh-UIFromRegistry {
    Update-DashboardCards
    $mm = Get-ItemProperty $mmPath -ErrorAction SilentlyContinue
    $gd = Get-ItemProperty $gdPath -ErrorAction SilentlyContinue

    $frag = if ($mm.MaxVideoMemoryFragmentationBuffer) { [int]$mm.MaxVideoMemoryFragmentationBuffer } else { $STOCK_FRAG_MB }
    $proc = if ($mm.MaxProcessBudgetCapBuffer) { [int]$mm.MaxProcessBudgetCapBuffer } else { $STOCK_PROC_MB }
    $shared = if ($mm.SystemPartitionCommitLimitPercentage) { [int]$mm.SystemPartitionCommitLimitPercentage } else { $STOCK_SHARED_PCT }

    $sldFragBuffer.Minimum = 32
    $sldFragBuffer.Maximum = $STOCK_FRAG_MB
    $sldFragBuffer.Value = [Math]::Max(32, [Math]::Min($STOCK_FRAG_MB, $frag))

    $sldProcCap.Minimum = 32
    $sldProcCap.Maximum = $STOCK_PROC_MB
    $sldProcCap.Value = [Math]::Max(32, [Math]::Min($STOCK_PROC_MB, $proc))

    $sldSharedRam.Minimum = 50
    $sldSharedRam.Maximum = 90
    $sldSharedRam.Value = [Math]::Max(50, [Math]::Min(90, $shared))

    if ($gd.HwSchMode -eq 1) { $cmbHags.SelectedIndex = 0 }
    else { $cmbHags.SelectedIndex = 1 }
}

$btnPresetRecommended.Add_Click({
    $sldFragBuffer.Value = 128
    $sldProcCap.Value = 32
    $sldSharedRam.Value = 75
    $hags = if ($cmbHags.SelectedIndex -eq 0) { 1 } else { 2 }
    Apply-Settings -frag 128 -proc 32 -shared 75 -hags $hags
})

$btnPresetAggressive.Add_Click({
    $sldFragBuffer.Value = 32
    $sldProcCap.Value = 32
    $sldSharedRam.Value = 80
    $hags = if ($cmbHags.SelectedIndex -eq 0) { 1 } else { 2 }
    Apply-Settings -frag 32 -proc 32 -shared 80 -hags $hags
})

$btnPresetDefault.Add_Click({
    $sldFragBuffer.Value = $STOCK_FRAG_MB
    $sldProcCap.Value = $STOCK_PROC_MB
    $sldSharedRam.Value = $STOCK_SHARED_PCT
    $cmbHags.SelectedIndex = 1
    Apply-Settings -frag $null -proc $null -shared $null -hags 2
})

$btnApply.Add_Click({
    $frag = [int]$sldFragBuffer.Value
    $proc = [int]$sldProcCap.Value
    $shared = [int]$sldSharedRam.Value
    $hags = if ($cmbHags.SelectedIndex -eq 0) { 1 } else { 2 }

    $fragVal = if ($frag -eq $STOCK_FRAG_MB) { $null } else { $frag }
    $procVal = if ($proc -eq $STOCK_PROC_MB) { $null } else { $proc }
    $sharedVal = if ($shared -eq $STOCK_SHARED_PCT) { $null } else { $shared }

    Apply-Settings -frag $fragVal -proc $procVal -shared $sharedVal -hags $hags
})

function Apply-Settings ($frag, $proc, $shared, $hags) {
    try {
        if (-not (Test-Path $mmPath)) {
            New-Item -Path $mmPath -Force | Out-Null
        }

        if ($null -eq $frag) {
            Remove-ItemProperty -Path $mmPath -Name "MaxVideoMemoryFragmentationBuffer" -ErrorAction SilentlyContinue
            Remove-ItemProperty -Path $mmPath -Name "MaxProcessBudgetCapBuffer" -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $mmPath -Name "MaxVideoMemoryFragmentationBuffer" -Value $frag -Type DWord
            Set-ItemProperty -Path $mmPath -Name "MaxProcessBudgetCapBuffer" -Value $proc -Type DWord
        }

        if ($null -eq $shared) {
            Remove-ItemProperty -Path $mmPath -Name "SystemPartitionCommitLimitPercentage" -ErrorAction SilentlyContinue
            Remove-ItemProperty -Path $mmPath -Name "SecondaryPartitionCommitLimitPercentage" -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $mmPath -Name "SystemPartitionCommitLimitPercentage" -Value $shared -Type DWord
            Set-ItemProperty -Path $mmPath -Name "SecondaryPartitionCommitLimitPercentage" -Value $shared -Type DWord
        }

        if ($null -ne $hags) {
            if (-not (Test-Path $gdPath)) { New-Item -Path $gdPath -Force | Out-Null }
            Set-ItemProperty -Path $gdPath -Name "HwSchMode" -Value $hags -Type DWord
        }

        Refresh-UIFromRegistry
        $txtActionNotice.Text = "Settings applied successfully! Restart Windows to activate changes."
    } catch {
        [System.Windows.MessageBox]::Show("Error saving registry settings: $($_.Exception.Message)", "Error", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error)
    }
}

$btnRefresh.Add_Click({
    Refresh-UIFromRegistry
    $txtActionNotice.Text = "Status refreshed from hardware."
})

$btnReboot.Add_Click({
    $res = [System.Windows.MessageBox]::Show("Do you want to restart your computer right now to apply the new memory settings?", "Restart Windows", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Question)
    if ($res -eq [System.Windows.MessageBoxResult]::Yes) {
        Restart-Computer -Force
    }
})

# Automatic Benchmark (NO USER INPUT)
$btnRunVramTest.Add_Click({
    $btnRunVramTest.IsEnabled = $false
    $btnStopVramTest.IsEnabled = $true
    $txtTestLog.Text = "Starting automatic benchmark across full memory pool...`r`n"
    $pbVramTest.Value = 0

    $logCb = [Action[string]]{
        param($msg)
        $window.Dispatcher.Invoke([Action]{
            $txtTestLog.AppendText("$msg`r`n")
            $txtTestLog.ScrollToEnd()
        })
    }

    $progCb = [Action[int]]{
        param($pct)
        $window.Dispatcher.Invoke([Action]{
            $pbVramTest.Value = $pct
        })
    }

    $finCb = [Action]{
        $window.Dispatcher.Invoke([Action]{
            $btnRunVramTest.IsEnabled = $true
            $btnStopVramTest.IsEnabled = $false
            Update-DashboardCards
        })
    }

    $targetIdx = if ($null -ne $script:selectedGpu) { $script:selectedGpu.Index } else { -1 }
    $thread = New-Object System.Threading.Thread([System.Threading.ThreadStart]{
        [VramBenchmark]::RunFullBenchmark($targetIdx, $logCb, $progCb, $finCb)
    })
    $thread.IsBackground = $true
    $thread.Start()
})

$btnStopVramTest.Add_Click({
    [VramBenchmark]::CancelRequested = $true
    $btnStopVramTest.IsEnabled = $false
    $txtTestLog.AppendText("Halt signal sent. Releasing all resident buffers...`r`n")
})

# Initial setup
Set-Theme $true
Refresh-UIFromRegistry

$window.ShowDialog() | Out-Null
