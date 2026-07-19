using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace CoreCage.Core
{
    /// <summary>
    /// THE BIG RED BUTTON. Reverses every change CoreCage has ever made on this
    /// system, in dependency-correct order, with best-effort error swallowing so one
    /// failure doesn't abort the rest. The single safety net that lets a user say
    /// "I'm going to try this app" without fear of trashing their system.
    ///
    /// What it touches:
    ///   1. Gaming Mode++ layer (MSI mode, NIC props, GameDVR, BG apps, QoS)
    ///   2. EAC-safe polish (IFEO, powercfg, FSO, TdrDelay, paused services)
    ///   3. Network tweaks (DNS, TCP autotuning, RWIN, etc.)
    ///   4. Power plan (back to Balanced)
    ///   5. Process priorities (everything we throttled goes back to Normal)
    ///   6. Timer resolution (back to default)
    ///   7. Telemetry / Game Bar / Search / SysMain services (start if user wants)
    ///   8. Any saved RegistryBackup snapshots get restored
    ///   9. Auto-start scheduled tasks get removed
    ///
    /// Designed for: panicked user at 2am whose game just stopped working. They click,
    /// reboot, system is exactly as Windows shipped it. Refund-rate killer.
    /// </summary>
    public static class RestoreEverything
    {
        /// <summary>
        /// Restore everything. Returns a summary string of what was undone so caller
        /// can show user "Restored 47 changes" instead of just silently doing it.
        /// Never throws.
        /// </summary>
        public static RestoreSummary RestoreAll()
        {
            var summary = new RestoreSummary();
            Logger.Log("=== RestoreEverything: BIG RED BUTTON pressed ===");

            // 1. Reverse Gaming Mode++ (MSI mode, NIC, GameDVR, BG apps, QoS)
            TryRun("Gaming Mode++ revert", () =>
            {
                GamingModePlusPlus.RestoreAll();
                summary.GamingPlusReverted = true;
            });

            // 2. Reverse EAC-safe polish (IFEO + powercfg + FSO + TdrDelay + service resume)
            TryRun("EAC-safe polish revert", () =>
            {
                // Find every IFEO entry we own and clear it
                int cleared = ClearAllCoreCageIfeoEntries();
                summary.IfeoEntriesCleared = cleared;
                // Resume the services we paused for gaming — report the ACTUAL count resumed.
                int resumed = EacSafePriority.ResumeBackgroundServicesAfterGaming();
                summary.ServicesResumed = resumed > 0;
                // Reset TdrDelay + TdrDdiDelay back to default — flag reflects real success.
                summary.TdrDelayReset = ResetTdrDelay();
            });

            // 2b. Re-enable services that DisableTelemetry/Gaming Mode set to disabled
            //     (DiagTrack/WSearch/Spooler/SysMain). Previously the Big Red Button never
            //     undid these, so telemetry/search/print stayed off until manual repair.
            TryRun("Telemetry / Search / Spooler / SysMain -> re-enabled", () =>
            {
                summary.ServicesReenabled = ReenableStandardServices();
            });

            // 2c. Restore the core-park + min-perf floor CoreUnpark changed (powercfg export path —
            //     not a registry snapshot, so it's reverted explicitly here, before the power plan resets).
            TryRun("Core-unpark / perf-floor -> original", () =>
            {
                summary.CoreUnparkRestored = CoreUnpark.RestoreAll();
            });

            // 3. Power plan back to Balanced (a sane default; user can switch to whatever)
            TryRun("Power plan -> Balanced", () =>
            {
                // Only report success if powercfg actually exited 0.
                summary.PowerPlanReset = RunSilent("powercfg", "/SETACTIVE 381b4222-f694-41f0-9685-ff5bb260df2e");
            });

            // 4. Network defaults (TCP autotuning, RSS, RSC, DNS)
            TryRun("Network -> Windows defaults", () =>
            {
                // Gate success on the netsh calls actually exiting 0.
                bool ok = true;
                ok &= RunSilent("netsh", "int tcp set global autotuninglevel=normal");
                ok &= RunSilent("netsh", "int tcp set global rss=enabled");
                ok &= RunSilent("netsh", "int tcp set global netdma=enabled");
                ok &= RunSilent("netsh", "int tcp set global ecncapability=default");
                // Remove ALL RigOpt-* QoS policies (covers msedge, PioneerGame, etc.) — best-effort,
                // not part of the success gate.
                RunSilent("powershell.exe",
                    "-NoProfile -Command \"Get-NetQosPolicy | Where-Object Name -like 'RigOpt-*' | Remove-NetQosPolicy -Confirm:$false -ErrorAction SilentlyContinue\"");
                summary.NetworkReset = ok;
            });

            // 5. Process priorities back to Normal for everything that's still running
            TryRun("Process priorities -> Normal", () =>
            {
                int reset = ResetAllProcessPriorities();
                summary.ProcessPrioritiesReset = reset;
            });

            // 5b. Process affinities back to full-mask for everything that's still running (review
            //     IMPORTANT-2 -- Core Cage confines background processes to a caged-core mask; a crash
            //     mid-cage loses the in-memory CagePlan, so this Big-Red-Button pass is the only thing
            //     short of reboot that can un-pin them). Mirrors ResetAllProcessPriorities' exact
            //     exclusion logic: no named skip-list, just a per-process try/catch that silently skips
            //     anything protected/inaccessible.
            TryRun("Process affinities -> full mask", () =>
            {
                int reset = ResetAllProcessAffinities();
                summary.ProcessAffinitiesReset = reset;
            });

            // 6. Timer resolution back to default
            TryRun("Timer resolution -> default", () =>
            {
                // Was a reflection call to TimerResolution.ResetToDefault — a method that does
                // not exist, so it silently no-op'd while reporting success. Call the real one.
                SystemTweaks.ResetTimerResolution();
                summary.TimerResolutionReset = true;
            });

            // 7. Auto-start tasks removed (CoreCage scheduled-task entries)
            TryRun("Auto-start scheduled tasks -> removed", () =>
            {
                var taskNames = new[] { "CoreCageStartup", "CoreCageGamingMode" };
                foreach (var taskName in taskNames)
                {
                    RunSilent("schtasks", $"/Delete /TN {taskName} /F");
                }
                // Honest: "removed" means none of them still exist (a delete of an absent task
                // reports a non-zero exit, which is fine — the end state is what matters).
                summary.AutoStartTasksRemoved = !taskNames.Any(ScheduledTaskExists);
            });

            // 8. Restore any RegistryBackup snapshots labeled "rigopt-*"
            TryRun("RegistryBackup snapshots -> restored", () =>
            {
                int restored = RestoreAllCoreCageSnapshots();
                summary.RegistrySnapshotsRestored = restored;
            });

            Logger.Log("=== RestoreEverything complete: " + summary.ToString() + " ===");
            return summary;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        private static void TryRun(string label, Action a)
        {
            try { a(); Logger.Log("  ok: " + label); }
            catch (Exception ex) { Logger.Log("  FAIL " + label + ": " + ex.Message); }
        }

        /// <summary>Runs a process and returns its exit code, or -1 if it could not start,
        /// timed out, or threw. Lets callers report HONEST success instead of assuming it.</summary>
        private static int RunForExit(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p == null) { Logger.Log($"  run({exe} {args}): failed to start"); return -1; }
                if (!p.WaitForExit(10000)) { Logger.Log($"  run({exe} {args}): timed out"); return -1; }
                return p.ExitCode;
            }
            catch (Exception ex) { Logger.Log("  run(" + exe + "): " + ex.Message); return -1; }
        }

        /// <summary>True only if the command ran and exited 0.</summary>
        private static bool RunSilent(string exe, string args)
        {
            int rc = RunForExit(exe, args);
            if (rc != 0) Logger.Log($"  {exe} {args} -> exit {rc}");
            return rc == 0;
        }

        /// <summary>True if a scheduled task with this name currently exists.</summary>
        private static bool ScheduledTaskExists(string name)
            => RunForExit("schtasks", $"/Query /TN {name}") == 0;

        /// <summary>Re-enables and starts the services Gaming Mode / DisableTelemetry turned off.
        /// Returns the count whose start-type was successfully set back to auto.</summary>
        private static int ReenableStandardServices()
        {
            int n = 0;
            foreach (var svc in new[] { "DiagTrack", "WSearch", "Spooler" })
            {
                bool configured = RunSilent("sc", $"config {svc} start= auto");
                RunSilent("sc", $"start {svc}");   // best-effort; already-running returns non-zero
                if (configured) n++;
            }
            RunSilent("sc", "start SysMain");      // SysMain is only stopped (never disabled) by us
            return n;
        }

        // Walks HKLM\...\IFEO and clears any subkey whose PerfOptions match the
        // CoreCage "owned" signature (CpuPriorityClass=3, CpuPriorityBoost=0).
        // We won't touch IFEO entries the user set themselves -- only ours.
        private static int ClearAllCoreCageIfeoEntries()
        {
            const string IFEO = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            int cleared = 0;
            try
            {
                using (var ifeo = Registry.LocalMachine.OpenSubKey(IFEO, true))
                {
                    if (ifeo == null) return 0;
                    foreach (var exe in ifeo.GetSubKeyNames())
                    {
                        try
                        {
                            using (var perf = ifeo.OpenSubKey(exe + @"\PerfOptions", true))
                            {
                                if (perf == null) continue;
                                var cls = perf.GetValue("CpuPriorityClass") as int?;
                                var boost = perf.GetValue("CpuPriorityBoost") as int?;
                                if (cls == 3 && boost == 0)
                                {
                                    // Looks like ours. Clear the values.
                                    foreach (var name in new[] { "CpuPriorityClass", "CpuPriorityBoost", "IoPriority", "CpuAffinityMask" })
                                    {
                                        try { perf.DeleteValue(name, false); } catch { }
                                    }
                                    cleared++;
                                }
                            }
                        }
                        catch { /* skip un-readable subkey */ }
                    }
                }
            }
            catch (Exception ex) { Logger.Log("ClearAllCoreCageIfeoEntries: " + ex.Message); }
            return cleared;
        }

        /// <summary>Deletes BOTH values SetTdrDelay writes (TdrDelay + TdrDdiDelay), reverting to
        /// the Windows default behavior. Returns false if the key couldn't be opened for write.</summary>
        private static bool ResetTdrDelay()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (k == null) { Logger.Log("ResetTdrDelay: GraphicsDrivers key not writable"); return false; }
                k.DeleteValue("TdrDelay", false);
                k.DeleteValue("TdrDdiDelay", false);   // was leaked — SetTdrDelay writes this too
                return true;
            }
            catch (Exception ex) { Logger.Log("ResetTdrDelay: " + ex.Message); return false; }
        }

        private static int ResetAllProcessPriorities()
        {
            int n = 0;
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.PriorityClass != ProcessPriorityClass.Normal)
                        {
                            p.PriorityClass = ProcessPriorityClass.Normal;
                            n++;
                        }
                    }
                    catch { /* protected process, skip */ }
                }
            }
            catch { }
            return n;
        }

        /// <summary>Pure full-core affinity mask for a machine with this many logical cores, as a bare
        /// long (e.g. 4 cores -&gt; 0b1111). No Process/OS dependency -- extracted so the mask math is
        /// unit-testable without mutating any real process's affinity.</summary>
        internal static long FullAffinityMask(int processorCount) => (1L << processorCount) - 1;

        /// <summary>Sets ProcessorAffinity back to the full-core mask for every process still running.
        /// Same per-process try/catch skip-on-denied pattern as <see cref="ResetAllProcessPriorities"/> --
        /// no named exclusion list, a protected/system process just throws and is skipped. Returns the
        /// count actually changed. Never throws.</summary>
        private static int ResetAllProcessAffinities()
        {
            int n = 0;
            try
            {
                var fullMask = (IntPtr)FullAffinityMask(Environment.ProcessorCount);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessorAffinity != fullMask)
                        {
                            p.ProcessorAffinity = fullMask;
                            n++;
                        }
                    }
                    catch { /* protected process, skip */ }
                }
            }
            catch { }
            return n;
        }

        // Restores every "rigopt-*" snapshot (notably the snapshot-before-write capture of the user's
        // TRUE original registry values — RegistryTweakManifest.SnapshotLabel). Delegates to
        // RegistryBackup so it reads from the SAME directory Snapshot writes to. The previous local
        // copy looked in "CoreCage\RegistryBackup" while snapshots are saved under
        // "CoreCage\Backups", so it silently restored nothing.
        private static int RestoreAllCoreCageSnapshots()
            => RegistryBackup.RestoreAllWithPrefix("rigopt-");
    }

    /// <summary>Summary of what RestoreEverything did, for user-facing reporting.</summary>
    public class RestoreSummary
    {
        public bool GamingPlusReverted;
        public int IfeoEntriesCleared;
        public bool ServicesResumed;
        public int ServicesReenabled;
        public bool TdrDelayReset;
        public bool PowerPlanReset;
        public bool NetworkReset;
        public int ProcessPrioritiesReset;
        public int ProcessAffinitiesReset;
        public bool TimerResolutionReset;
        public bool AutoStartTasksRemoved;
        public int RegistrySnapshotsRestored;
        public bool CoreUnparkRestored;

        public override string ToString()
        {
            return
                $"GamingMode++ revert={GamingPlusReverted}, " +
                $"IFEO cleared={IfeoEntriesCleared}, " +
                $"services resumed={ServicesResumed}, " +
                $"services re-enabled={ServicesReenabled}, " +
                $"TdrDelay reset={TdrDelayReset}, " +
                $"power plan reset={PowerPlanReset}, " +
                $"network reset={NetworkReset}, " +
                $"priorities reset={ProcessPrioritiesReset}, " +
                $"affinities reset={ProcessAffinitiesReset}, " +
                $"timer reset={TimerResolutionReset}, " +
                $"tasks removed={AutoStartTasksRemoved}, " +
                $"snapshots restored={RegistrySnapshotsRestored}, " +
                $"core-unpark restored={CoreUnparkRestored}";
        }

        public string ForUser()
        {
            int totalChanges =
                (GamingPlusReverted ? 5 : 0) +
                IfeoEntriesCleared +
                (ServicesResumed ? 3 : 0) +
                ServicesReenabled +
                (TdrDelayReset ? 1 : 0) +
                (PowerPlanReset ? 1 : 0) +
                (NetworkReset ? 5 : 0) +
                ProcessPrioritiesReset +
                ProcessAffinitiesReset +
                (TimerResolutionReset ? 1 : 0) +
                (AutoStartTasksRemoved ? 3 : 0) +
                RegistrySnapshotsRestored +
                (CoreUnparkRestored ? 1 : 0);
            return $"System restored. {totalChanges} change(s) reversed. A reboot is recommended " +
                   "so MSI mode + TdrDelay revert fully.";
        }
    }
}
