using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace VRAMOptimizerApp {
    public class WddmRegistryState {
        public int? CustomFragMb { get; set; }
        public int? CustomProcMb { get; set; }
        public int? CustomSharedPct { get; set; }
        public int? CustomSecondarySharedPct { get; set; }
        public int? HwSchMode { get; set; }

        public int EffectiveFragCapMb {
            get { return CustomFragMb.HasValue ? WddmCalculator.ClampBufferCapMb(CustomFragMb.Value) : WddmCalculator.STOCK_MAX_FRAG_MB; }
        }

        public int EffectiveProcCapMb {
            get { return CustomProcMb.HasValue ? WddmCalculator.ClampBufferCapMb(CustomProcMb.Value) : WddmCalculator.STOCK_MAX_PROC_MB; }
        }

        public int EffectiveSharedPct {
            get {
                if (!CustomSharedPct.HasValue) return WddmCalculator.STOCK_SHARED_PCT;
                return Math.Max(WddmCalculator.MIN_SHARED_PCT, Math.Min(WddmCalculator.MAX_SHARED_PCT, CustomSharedPct.Value));
            }
        }

        public int EffectiveHagsMode {
            get { return HwSchMode ?? 2; }
        }
    }

    public static class RegistryManager {
        public const string MM_PATH = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MemoryManager";
        public const string GD_PATH = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public static WddmRegistryState ReadCurrentState() {
            WddmRegistryState state = new WddmRegistryState();

            using (RegistryKey mm = Registry.LocalMachine.OpenSubKey(MM_PATH, false)) {
                if (mm != null) {
                    state.CustomFragMb = ReadNullableInt(mm, "MaxVideoMemoryFragmentationBuffer");
                    state.CustomProcMb = ReadNullableInt(mm, "MaxProcessBudgetCapBuffer");
                    state.CustomSharedPct = ReadNullableInt(mm, "SystemPartitionCommitLimitPercentage");
                    state.CustomSecondarySharedPct = ReadNullableInt(mm, "SecondaryPartitionCommitLimitPercentage");
                }
            }

            using (RegistryKey gd = Registry.LocalMachine.OpenSubKey(GD_PATH, false)) {
                if (gd != null) {
                    state.HwSchMode = ReadNullableInt(gd, "HwSchMode");
                }
            }

            return state;
        }

        private static int? ReadNullableInt(RegistryKey key, string valueName) {
            object raw = key.GetValue(valueName);
            if (raw == null) return null;
            try {
                return Convert.ToInt32(raw);
            } catch {
                return null;
            }
        }

        /// <summary>
        /// Writes or removes each WDDM setting independently so mixed stock + custom slider combinations
        /// never collide or throw Nullable exceptions.
        /// </summary>
        public static void ApplySettings(int? fragMb, int? procMb, int? sharedPct, int? hagsMode) {
            using (RegistryKey mm = Registry.LocalMachine.CreateSubKey(MM_PATH)) {
                if (mm == null) {
                    throw new InvalidOperationException("Unable to open or create HKLM\\" + MM_PATH);
                }

                if (fragMb == null) {
                    mm.DeleteValue("MaxVideoMemoryFragmentationBuffer", false);
                } else {
                    int clampedFrag = WddmCalculator.ClampBufferCapMb(fragMb.Value);
                    mm.SetValue("MaxVideoMemoryFragmentationBuffer", clampedFrag, RegistryValueKind.DWord);
                }

                if (procMb == null) {
                    mm.DeleteValue("MaxProcessBudgetCapBuffer", false);
                } else {
                    int clampedProc = WddmCalculator.ClampBufferCapMb(procMb.Value);
                    mm.SetValue("MaxProcessBudgetCapBuffer", clampedProc, RegistryValueKind.DWord);
                }

                if (sharedPct == null) {
                    mm.DeleteValue("SystemPartitionCommitLimitPercentage", false);
                    mm.DeleteValue("SecondaryPartitionCommitLimitPercentage", false);
                } else {
                    int clampedShared = Math.Max(WddmCalculator.MIN_SHARED_PCT, Math.Min(WddmCalculator.MAX_SHARED_PCT, sharedPct.Value));
                    mm.SetValue("SystemPartitionCommitLimitPercentage", clampedShared, RegistryValueKind.DWord);
                    mm.SetValue("SecondaryPartitionCommitLimitPercentage", clampedShared, RegistryValueKind.DWord);
                }
            }

            if (hagsMode.HasValue) {
                using (RegistryKey gd = Registry.LocalMachine.CreateSubKey(GD_PATH)) {
                    if (gd == null) {
                        throw new InvalidOperationException("Unable to open or create HKLM\\" + GD_PATH);
                    }
                    gd.SetValue("HwSchMode", hagsMode.Value == 1 ? 1 : 2, RegistryValueKind.DWord);
                }
            }
        }

        public static string EnsureStartupSnapshot() {
            try {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string backupDir = Path.Combine(appDir, "Backups");
                if (!Directory.Exists(backupDir)) {
                    Directory.CreateDirectory(backupDir);
                }
                string snapshotPath = Path.Combine(backupDir, "FirstLaunch_Registry_Snapshot.reg");
                if (!File.Exists(snapshotPath)) {
                    ExportRegistryFile(snapshotPath);
                }
                return snapshotPath;
            } catch (Exception ex) {
                Trace.WriteLine("Unable to create startup registry snapshot: " + ex.Message);
                return null;
            }
        }

        public static void ExportRegistryFile(string filePath) {
            WddmRegistryState state = ReadCurrentState();
            string content = WddmCalculator.BuildRegistryBackupContent(
                state.CustomFragMb,
                state.CustomProcMb,
                state.CustomSharedPct,
                state.CustomSecondarySharedPct,
                state.HwSchMode
            );
            File.WriteAllText(filePath, content, Encoding.Unicode);
        }

        public static void RestoreRegistryFile(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) {
                throw new FileNotFoundException("Backup .reg file not found.", filePath);
            }

            string content = File.ReadAllText(filePath);
            string validationError;
            if (!WddmCalculator.ValidateBackupRegContent(content, out validationError)) {
                throw new InvalidDataException(validationError);
            }

            ProcessStartInfo psi = new ProcessStartInfo("reg.exe", string.Format("import \"{0}\"", filePath));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi)) {
                if (p == null) {
                    throw new InvalidOperationException("Failed to start reg.exe.");
                }
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(10000)) {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("reg.exe import timed out after 10 seconds.");
                }
                if (p.ExitCode != 0) {
                    string details = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                    throw new InvalidOperationException(string.Format("reg.exe exited with code {0}: {1}", p.ExitCode, details));
                }
            }
        }
    }
}
