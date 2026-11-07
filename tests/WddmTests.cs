using System;

namespace VRAMOptimizerApp.Tests {
    public static class WddmTests {
        private static int passed = 0;
        private static int failed = 0;

        public static int Main() {
            Console.WriteLine("Running Windows WDDM & VRAM Optimizer Unit Test Suite...");
            Console.WriteLine("--------------------------------------------------------");

            TestKernelClampFloor();
            TestSixGbDiscreteGpuMath();
            TestFourGbDiscreteGpuSubCapMath();
            TestTwentyFourGbDiscreteGpuCapMath();
            TestUmaVsDiscreteClassification();
            TestTotalPoolNeverDoubleCountsUma();
            TestRegistryBackupSerialization();
            TestRegistryRestoreValidationSecurity();
            TestD3D11ComInterfaceSingleSlot();

            Console.WriteLine("--------------------------------------------------------");
            Console.WriteLine(string.Format("Results: {0} passed, {1} failed.", passed, failed));
            return failed == 0 ? 0 : 1;
        }

        private static void AssertEqual<T>(T expected, T actual, string name) {
            if ( Equals(expected, actual) ) {
                passed++;
                Console.WriteLine("  [PASS] " + name);
            } else {
                failed++;
                Console.WriteLine(string.Format("  [FAIL] {0}: expected '{1}', got '{2}'", name, expected, actual));
            }
        }

        private static void AssertNear(double expected, double actual, double tol, string name) {
            if (Math.Abs(expected - actual) <= tol) {
                passed++;
                Console.WriteLine("  [PASS] " + name);
            } else {
                failed++;
                Console.WriteLine(string.Format("  [FAIL] {0}: expected {1:F2}, got {2:F2}", name, expected, actual));
            }
        }

        private static void TestKernelClampFloor() {
            AssertEqual(32, WddmCalculator.ClampBufferCapMb(0), "Kernel clamp floors 0 MiB to 32 MiB");
            AssertEqual(32, WddmCalculator.ClampBufferCapMb(16), "Kernel clamp floors 16 MiB to 32 MiB");
            AssertEqual(128, WddmCalculator.ClampBufferCapMb(128), "Kernel clamp preserves 128 MiB");
        }

        private static void TestSixGbDiscreteGpuMath() {
            // RTX 4050 Laptop GPU: 5920 MiB Windows Dedicated VRAM
            const double vram = 5920.0;
            AssertEqual(768, WddmCalculator.ComputeStockTotalReserveMb(vram), "6 GB GPU stock reserve = 768 MiB (512+256)");
            AssertNear(5152.0, WddmCalculator.ComputeStockDedicatedBudgetMb(vram), 0.01, "6 GB GPU stock budget = 5152 MiB");
            AssertEqual(160, WddmCalculator.ComputeEffectiveTotalReserveMb(vram, 128, 32), "6 GB GPU Recommended (128+32) = 160 MiB (+608 MiB)");
            AssertEqual(64, WddmCalculator.ComputeEffectiveTotalReserveMb(vram, 32, 32), "6 GB GPU Aggressive (32+32) = 64 MiB (+704 MiB)");
        }

        private static void TestFourGbDiscreteGpuSubCapMath() {
            // 4 GB GPU (3968 MiB Dedicated): 10% = 396 MiB (< 512), 5% = 198 MiB (< 256)
            const double vram = 3968.0;
            AssertEqual(396, WddmCalculator.ComputeEffectiveFragReserveMb(vram, null), "4 GB GPU stock frag reserve = 396 MiB (10% floor, not 512)");
            AssertEqual(198, WddmCalculator.ComputeEffectiveProcReserveMb(vram, null), "4 GB GPU stock proc reserve = 198 MiB (5% floor, not 256)");
            AssertEqual(396, WddmCalculator.ComputeEffectiveFragReserveMb(vram, 448), "4 GB GPU setting 448 MiB cap stays 396 MiB (0 false savings)");
            AssertEqual(128, WddmCalculator.ComputeEffectiveFragReserveMb(vram, 128), "4 GB GPU setting 128 MiB cap lowers reserve to 128 MiB");
        }

        private static void TestTwentyFourGbDiscreteGpuCapMath() {
            // 24 GB GPU (24576 MiB): 10% = 2457 -> capped at 512; 5% = 1228 -> capped at 256
            const double vram = 24576.0;
            AssertEqual(768, WddmCalculator.ComputeStockTotalReserveMb(vram), "24 GB GPU stock reserve = 768 MiB");
        }

        private static void TestUmaVsDiscreteClassification() {
            ulong mb = 1048576UL;
            // Live numbers from AMD Radeon 680M on ASUS TUF A15: 483.46 MiB Dedicated, 28735 MiB LOCAL, 0 NON_LOCAL
            bool radeonUma = WddmCalculator.IsUnifiedMemoryAdapter(483.46, 28735UL * mb, 0UL, true);
            AssertEqual(true, radeonUma, "AMD Radeon 680M classified as UMA");

            // Live numbers from NVIDIA RTX 4050 Laptop: 5920 MiB Dedicated, 5856 MiB LOCAL, 28251 MiB NON_LOCAL
            bool rtxUma = WddmCalculator.IsUnifiedMemoryAdapter(5920.0, 5856UL * mb, 28251UL * mb, true);
            AssertEqual(false, rtxUma, "NVIDIA RTX 4050 classified as Discrete PCIe");
        }

        private static void TestTotalPoolNeverDoubleCountsUma() {
            var umaGpu = new VramBenchmark.GpuAdapterInfo {
                IsUma = true,
                DedicatedMb = 483.46,
                LocalBudgetMb = 28734.88,
                SharedMaxMb = 28795.42,
                NonLocalBudgetMb = 0.0
            };
            AssertNear(28734.88, umaGpu.TotalPoolBudgetMb, 0.01, "UMA TotalPoolBudgetMb equals LocalBudgetMb (never 56 GiB)");

            var dgpu = new VramBenchmark.GpuAdapterInfo {
                IsUma = false,
                DedicatedMb = 5920.0,
                LocalBudgetMb = 5856.0,
                NonLocalBudgetMb = 28251.42
            };
            AssertNear(34107.42, dgpu.TotalPoolBudgetMb, 0.01, "Discrete TotalPoolBudgetMb sums Local + NonLocal");
        }

        private static void TestRegistryBackupSerialization() {
            string custom = WddmCalculator.BuildRegistryBackupContent(128, 32, 75, 75, 2);
            AssertEqual(true, custom.Contains("\"MaxVideoMemoryFragmentationBuffer\"=dword:00000080"), "Backup serializes 128 as dword:00000080");
            AssertEqual(true, custom.Contains("\"MaxProcessBudgetCapBuffer\"=dword:00000020"), "Backup serializes 32 as dword:00000020");

            string stock = WddmCalculator.BuildRegistryBackupContent(null, null, null, null, null);
            AssertEqual(true, stock.Contains("\"MaxVideoMemoryFragmentationBuffer\"=-"), "Stock backup emits '-' deletion for Frag");
            AssertEqual(true, stock.Contains("\"HwSchMode\"=-"), "Stock backup emits '-' deletion for absent HwSchMode");
        }

        private static void TestRegistryRestoreValidationSecurity() {
            string valid = WddmCalculator.BuildRegistryBackupContent(128, 32, 75, 75, 2);
            string reason;
            AssertEqual(true, WddmCalculator.ValidateBackupRegContent(valid, out reason), "Valid GraphicsDrivers .reg accepted");

            string malicious = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run]\r\n\"Evil\"=\"cmd.exe\"";
            AssertEqual(false, WddmCalculator.ValidateBackupRegContent(malicious, out reason), "Arbitrary Run key .reg rejected");

            AssertEqual(false, WddmCalculator.ValidateBackupRegContent("not a reg file", out reason), "Invalid header rejected");
        }

        private static void TestD3D11ComInterfaceSingleSlot() {
            int methodCount = typeof(VramBenchmark.ID3D11Device).GetMethods().Length;
            AssertEqual(1, methodCount, "ID3D11Device COM interface declares exactly 1 method (slot 3 CreateBuffer)");
        }
    }
}
