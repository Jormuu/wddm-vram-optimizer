using System;
using System.Text;

namespace VRAMOptimizerApp {
    /// <summary>
    /// Pure, deterministic WDDM (dxgmms2.sys) budget math, UMA classification,
    /// and .reg backup serialization/validation helpers.
    /// </summary>
    public static class WddmCalculator {
        public const int MIN_BUFFER_MB = 32;
        public const int STOCK_MAX_FRAG_MB = 512;
        public const int STOCK_MAX_PROC_MB = 256;
        public const int STOCK_FRAG_PCT = 10;
        public const int STOCK_PROC_PCT = 5;
        public const int STOCK_SHARED_PCT = 50;
        public const int MIN_SHARED_PCT = 50;
        public const int MAX_SHARED_PCT = 90;

        /// <summary>
        /// Clamps MaxVideoMemoryFragmentationBuffer / MaxProcessBudgetCapBuffer the same way
        /// dxgmms2.sys (VIDMM_GLOBAL::InitGlobals) clamps REG_DWORD values (>= 32 MiB).
        /// </summary>
        public static int ClampBufferCapMb(int valueMb) {
            return Math.Max(MIN_BUFFER_MB, valueMb);
        }

        /// <summary>
        /// Computes the effective WDDM Fragmentation Buffer in MiB for a discrete GPU:
        /// FragBuffer = min(floor(DedicatedVramMb * 10%), MaxVideoMemoryFragmentationBuffer).
        /// </summary>
        public static int ComputeEffectiveFragReserveMb(double dedicatedVramMb, int? customMaxFragMb) {
            if (dedicatedVramMb <= 0) return 0;
            int pctFloorMb = (int)Math.Floor(dedicatedVramMb * (STOCK_FRAG_PCT / 100.0));
            int capMb = customMaxFragMb.HasValue ? ClampBufferCapMb(customMaxFragMb.Value) : STOCK_MAX_FRAG_MB;
            return Math.Min(pctFloorMb, capMb);
        }

        /// <summary>
        /// Computes the effective WDDM Process Budget Cap Buffer in MiB for a discrete GPU:
        /// ProcCapBuffer = min(floor(DedicatedVramMb * 5%), MaxProcessBudgetCapBuffer).
        /// </summary>
        public static int ComputeEffectiveProcReserveMb(double dedicatedVramMb, int? customMaxProcMb) {
            if (dedicatedVramMb <= 0) return 0;
            int pctFloorMb = (int)Math.Floor(dedicatedVramMb * (STOCK_PROC_PCT / 100.0));
            int capMb = customMaxProcMb.HasValue ? ClampBufferCapMb(customMaxProcMb.Value) : STOCK_MAX_PROC_MB;
            return Math.Min(pctFloorMb, capMb);
        }

        public static int ComputeStockTotalReserveMb(double dedicatedVramMb) {
            return ComputeEffectiveFragReserveMb(dedicatedVramMb, null)
                 + ComputeEffectiveProcReserveMb(dedicatedVramMb, null);
        }

        public static int ComputeEffectiveTotalReserveMb(double dedicatedVramMb, int? customMaxFragMb, int? customMaxProcMb) {
            return ComputeEffectiveFragReserveMb(dedicatedVramMb, customMaxFragMb)
                 + ComputeEffectiveProcReserveMb(dedicatedVramMb, customMaxProcMb);
        }

        public static double ComputeStockDedicatedBudgetMb(double dedicatedVramMb) {
            if (dedicatedVramMb <= 0) return 0;
            return Math.Max(0, dedicatedVramMb - ComputeStockTotalReserveMb(dedicatedVramMb));
        }

        /// <summary>
        /// Detects Unified Memory Architecture (integrated GPUs such as AMD Radeon 680M / Intel Iris Xe),
        /// where DXGI reports the shared system RAM pool inside LOCAL (segment 0) and 0 budget in NON_LOCAL (segment 1).
        /// </summary>
        public static bool IsUnifiedMemoryAdapter(double descDedicatedMb, ulong localBudgetBytes, ulong nonLocalBudgetBytes, bool hasQueryVideoMemoryInfo) {
            if (!hasQueryVideoMemoryInfo) {
                return descDedicatedMb <= 0;
            }
            double localBudgetMb = localBudgetBytes / 1048576.0;
            if (nonLocalBudgetBytes == 0 && (descDedicatedMb <= 0 || localBudgetMb > descDedicatedMb * 1.25)) {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Formats a deterministic Windows Registry Editor Version 5.00 (.reg) snapshot
        /// covering MemoryManager and HwSchMode values (emitting '-' to remove absent keys on restore).
        /// </summary>
        public static string BuildRegistryBackupContent(int? frag, int? proc, int? sysCommitPct, int? secCommitPct, int? hagsMode) {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine();
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MemoryManager]");
            AppendDwordOrDelete(sb, "MaxVideoMemoryFragmentationBuffer", frag);
            AppendDwordOrDelete(sb, "MaxProcessBudgetCapBuffer", proc);
            AppendDwordOrDelete(sb, "SystemPartitionCommitLimitPercentage", sysCommitPct);
            AppendDwordOrDelete(sb, "SecondaryPartitionCommitLimitPercentage", secCommitPct);
            sb.AppendLine();
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]");
            AppendDwordOrDelete(sb, "HwSchMode", hagsMode);
            return sb.ToString();
        }

        private static void AppendDwordOrDelete(StringBuilder sb, string name, int? value) {
            if (value.HasValue) {
                sb.AppendLine(string.Format("\"{0}\"=dword:{1:x8}", name, value.Value));
            } else {
                sb.AppendLine(string.Format("\"{0}\"=-", name));
            }
        }

        /// <summary>
        /// Validates that a .reg file to be imported as Administrator is a genuine registry script
        /// and only targets HKLM\...\GraphicsDrivers keys.
        /// </summary>
        public static bool ValidateBackupRegContent(string content, out string reason) {
            if (string.IsNullOrWhiteSpace(content)) {
                reason = "File is empty.";
                return false;
            }

            string trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (!trimmed.StartsWith("Windows Registry Editor Version 5.00", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("REGEDIT4", StringComparison.OrdinalIgnoreCase)) {
                reason = "Not a valid Windows .reg file header.";
                return false;
            }

            bool foundSection = false;
            string[] lines = trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string rawLine in lines) {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[")) {
                    if (!line.EndsWith("]")) {
                        reason = "Malformed registry key section header: " + line;
                        return false;
                    }
                    string sectionPath = line.Substring(1, line.Length - 2).Trim();
                    // Allow deleting or setting GraphicsDrivers and its MemoryManager subkey only
                    if (sectionPath.StartsWith("-")) sectionPath = sectionPath.Substring(1).Trim();
                    const string allowedRoot = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
                    if (!sectionPath.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                        !sectionPath.Equals(allowedRoot + @"\MemoryManager", StringComparison.OrdinalIgnoreCase)) {
                        reason = "Refusing to import unexpected registry path: [" + sectionPath + "]";
                        return false;
                    }
                    foundSection = true;
                }
            }

            if (!foundSection) {
                reason = "No GraphicsDrivers registry sections found in file.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
