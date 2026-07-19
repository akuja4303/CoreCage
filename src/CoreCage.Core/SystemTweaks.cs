using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Newtonsoft.Json;
using CoreCage.Core.Scheduling;

namespace CoreCage.Core
{
    public static class SystemTweaks
    {
        private static string? _originalPowerPlan;

        // ── Throttle snapshot ────────────────────────────────────────────────
        // ThrottleForMode bulk-drops background processes to Idle/BelowNormal and (in gaming)
        // cages them to cores 0-1. To reverse that EXACTLY — not a blunt "everything → Normal" —
        // we record each touched process's ORIGINAL priority + affinity BEFORE changing it, keyed
        // by PID. RestoreThrottledProcesses() plays it back. Mirrors the record-before-write pattern
        // used for NIC props. "First write wins" so the TRUE original survives a mode
        // re-throttle. In-memory by design: a single Apply→Restore happens within one app session
        // (game-exit auto-restore, mode switch, or the Restore button). The Big Red Button's blunt
        // ResetAllProcessPriorities remains the cross-session fallback.
        private static readonly Dictionary<int, (ProcessPriorityClass prio, IntPtr affinity)> _throttleSnapshot
            = new Dictionary<int, (ProcessPriorityClass, IntPtr)>();
        private static readonly object _throttleSnapshotLock = new object();

        // ── Crash-recovery persistence (#3b) ─────────────────────────────────
        // The in-memory snapshot above is lost if CoreCage is killed WHILE procs are throttled —
        // the original incident (machine left stranded at Idle). So we also mirror it to a JSON file
        // next to the registry snapshots (same dir/serializer). On startup RecoverThrottleSnapshotFromDisk()
        // un-strands any survivors from a crashed run; normal restore deletes the file.
        private static readonly string _throttleSnapshotPath =
            Path.Combine(RegistryBackup.BackupDirectory, "throttle-snapshot.json");

        private sealed class ThrottleEntry
        {
            public int Pid { get; set; }
            public string Name { get; set; } = "";          // guards against PID reuse across a crash
            public string Priority { get; set; } = "Normal"; // ProcessPriorityClass name
            public long Affinity { get; set; }               // 0 = leave affinity untouched on restore
        }
        
        // Timer resolution P/Invoke — correct NT signature:
        //   DesiredResolution : 100-ns units (5000 = 0.5 ms, 10000 = 1 ms)
        //   SetResolution     : TRUE to set, FALSE to query/restore
        //   CurrentResolution : receives the resolution actually in effect after the call
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        
        /// <summary>
        /// Applies the High Performance power plan using powercfg.
        /// </summary>
        public static void ApplyHighPerformancePowerPlan()
        {
            try
            {
                // First, get current power plan GUID
                var currentPlan = GetCurrentPowerPlan();
                if (string.IsNullOrEmpty(_originalPowerPlan))
                {
                    _originalPowerPlan = currentPlan;
                }
                
                // Set to High Performance (GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c)
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                
                using var process = Process.Start(psi);
                process?.WaitForExit();
                
                Logger.Log("Applied High Performance power plan");
            }
            catch (Exception ex)
            {
                Logger.LogError("ApplyHighPerformancePowerPlan failed", ex);
            }
        }
        
        /// <summary>
        /// Restores the original power plan.
        /// </summary>
        public static void RestorePowerPlan()
        {
            try
            {
                if (!string.IsNullOrEmpty(_originalPowerPlan))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = $"/setactive {_originalPowerPlan}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit();
                    
                    Logger.Log($"Restored power plan: {_originalPowerPlan}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("RestorePowerPlan failed", ex);
            }
        }
        
        /// <summary>
        /// Returns the GUID of the currently active power plan.
        /// Public so MainWindow can verify whether Ultimate Performance was successfully activated.
        /// </summary>
        public static string GetCurrentPowerPlanGuid()
        {
            return GetCurrentPowerPlan() ?? string.Empty;
        }

        /// <summary>
        /// Captures the current power plan into _originalPowerPlan without switching to any plan.
        /// Call this before any plan-switch so that RestorePowerPlan() returns to the right place.
        /// </summary>
        public static void CaptureCurrentPowerPlan()
        {
            if (string.IsNullOrEmpty(_originalPowerPlan))
                _originalPowerPlan = GetCurrentPowerPlan();
        }

        private static string? GetCurrentPowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(psi);
                string output = process?.StandardOutput.ReadToEnd() ?? "";
                process?.WaitForExit();

                // Output format: "Power Scheme GUID: xxxxxxxx-xxxx-... (Plan Name)"
                // We need the GUID, NOT the friendly name in parentheses.
                const string guidToken = "GUID:";
                int guidStart = output.IndexOf(guidToken, StringComparison.OrdinalIgnoreCase);
                if (guidStart >= 0)
                {
                    string afterToken = output.Substring(guidStart + guidToken.Length).Trim();
                    // GUID is the first 36 characters: 8-4-4-4-12
                    if (afterToken.Length >= 36)
                        return afterToken.Substring(0, 36).Trim();
                }
            }
            catch { }

            return null;
        }
        
        /// <summary>
        /// Sets the system timer resolution for lower latency.
        /// Uses both NtSetTimerResolution (0.5 ms) and timeBeginPeriod (1 ms) for maximum compatibility.
        /// Each timeBeginPeriod call is tracked so a single matching timeEndPeriod is called on reset.
        /// </summary>
        public static void SetTimerResolution(bool high)
        {
            try
            {
                // Method 1: NtSetTimerResolution (100-ns units: 5000 = 0.5 ms, 10000 = 1 ms)
                uint current;
                NtSetTimerResolution(high ? 5000u : 10000u, true, out current);
                Logger.Log($"Timer resolution (NtSetTimerResolution): {current / 10000.0:F2} ms");

                // Method 2: timeBeginPeriod for 1 ms (needed for legacy compatibility)
                // Only increment the reference count once; ResetTimerResolution will call timeEndPeriod exactly once.
                if (high && !_timerResolutionApplied)
                {
                    timeBeginPeriod(1);
                    _timerResolutionApplied = true;
                    Logger.Log("Timer resolution (timeBeginPeriod): 1 ms");
                }
                else if (!high && _timerResolutionApplied)
                {
                    timeEndPeriod(1);
                    _timerResolutionApplied = false;
                    Logger.Log("Timer resolution (timeBeginPeriod): restored to default");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("SetTimerResolution failed", ex);
            }
        }
        
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int timeBeginPeriod(int uPeriod);
        
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int timeEndPeriod(int uPeriod);
        
        private static bool _timerResolutionApplied;
        
        /// <summary>
        /// Resets timer resolution to default. Call on app exit.
        /// </summary>
        public static void ResetTimerResolution()
        {
            if (!_timerResolutionApplied) return;
            
            try
            {
                timeEndPeriod(1);
                _timerResolutionApplied = false;
                Logger.Log("Timer resolution reset to default");
            }
            catch (Exception ex)
            {
                Logger.LogError("ResetTimerResolution failed", ex);
            }
        }
        
        /// <summary>
        /// Disables Windows telemetry.
        /// </summary>
        public static void DisableTelemetry()
        {
            try
            {
                // Disable connected user experiences and telemetry service
                RunCommand("sc stop DiagTrack", ignoreErrors: true);
                RunCommand("sc config DiagTrack start= disabled", ignoreErrors: true);
                
                Logger.Log("Telemetry disabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("DisableTelemetry failed", ex);
            }
        }
        
        /// <summary>
        /// Disables Xbox Game Bar.
        /// </summary>
        public static void DisableGameBar()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", true);
                key?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                
                using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true);
                key2?.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                
                Logger.Log("Game Bar disabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("DisableGameBar failed", ex);
            }
        }
        
        /// <summary>
        /// Enables Game Mode.
        /// </summary>
        public static void EnableGameMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\GameBar", true);
                key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                
                Logger.Log("Game Mode enabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("EnableGameMode failed", ex);
            }
        }
        
        /// <summary>
        /// Disables Windows Search (SysMain).
        /// </summary>
        public static void DisableSearch()
        {
            try
            {
                RunCommand("sc stop WSearch", ignoreErrors: true);
                RunCommand("sc config WSearch start= disabled", ignoreErrors: true);
                
                Logger.Log("Windows Search disabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("DisableSearch failed", ex);
            }
        }
        
        /// <summary>
        /// Disables Print Spooler (if not printing).
        /// </summary>
        public static void DisablePrintSpooler()
        {
            try
            {
                RunCommand("sc stop Spooler", ignoreErrors: true);
                RunCommand("sc config Spooler start= disabled", ignoreErrors: true);
                
                Logger.Log("Print Spooler disabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("DisablePrintSpooler failed", ex);
            }
        }
        
        /// <summary>
        /// Mode-aware throttle: protects processes relevant to the active mode,
        /// drops everything else to BelowNormal so the OS scheduler deprioritizes them
        /// without the starvation risk of Idle.
        /// </summary>
        public static void ThrottleForMode(string mode)
        {
            bool isGaming = string.Equals(mode, "gaming", StringComparison.OrdinalIgnoreCase);

            // System processes that must never be touched regardless of mode.
            // Anti-cheat engines (EAC/BattlEye/Vanguard) are protected too: caging or
            // de-prioritising them can stutter or trip the game's integrity checks.
            var systemProtect = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Registry", "Idle",
                "smss", "csrss", "wininit", "winlogon", "services", "lsass",
                "svchost", "dwm", "explorer", "conhost", "spoolsv", "msiexec",
                "audiodg",      // audio engine — critical for in-game sound
                "CoreCage",
                "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "vgtray", // anti-cheat
                "EAAntiCheat.GameServiceLauncher", "EAAntiCheat.GameService",       // EA Javelin (BF6/BF2042)
            };

            // CPU AFFINITY CAGE (the arc-cage win, measured on the 5600G: 1% lows 48→85 fps,
            // stutter 25ms→15ms). In gaming mode, confine every non-protected background process
            // to logical cores 0-1, leaving cores 2-N uncontended for the game (complements the
            // EAC-safe IFEO affinity that gives the game 0xFFC). Any other mode releases
            // them back to all cores. Skipped on ≤2-core hosts (nowhere to cage to).
            int logicalCores = Environment.ProcessorCount;
            bool canCage      = logicalCores > 2;
            var  cageMask     = (IntPtr)AffinityMask.FromCores(new[] { 0, 1 }); // cores 0-1
            var  allCoresMask = (IntPtr)AffinityMask.AllCores(logicalCores);
            IntPtr affinityTarget = isGaming ? cageMask : allCoresMask;

            int selfPid   = Process.GetCurrentProcess().Id;
            int throttled = 0;
            int caged     = 0;
            int protected_ = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == selfPid) continue;
                    if (systemProtect.Contains(proc.ProcessName)) continue;
                    // Honor the canonical never-touch list (IDEs/dev tools, browsers, HW utilities).
                    // Fixes the over-reach where Gaming Mode buried VS Code/devenv at Idle: those are
                    // listed as untouchable in ProcessWatcher, but this throttle used to ignore that.
                    if (ProcessWatcher.IsProtectedSystemProcess(proc.ProcessName)) continue;

                    // Ask ProcessWatcher what category this process is
                    var cat = ProcessWatcher.ClassifyProcess(proc.ProcessName);

                    bool protect =
                        (isGaming && cat == ProcessCategory.Game) ||
                        UserProcessLists.IsListed(mode, proc.ProcessName);

                    if (protect)
                    {
                        protected_++;
                        continue;
                    }

                    // Snapshot this process's ORIGINAL priority + affinity ONCE, before we change
                    // anything, so RestoreThrottledProcesses can put it back exactly. Without this,
                    // the restore path can't know what to revert to and procs strand at Idle.
                    lock (_throttleSnapshotLock)
                    {
                        if (!_throttleSnapshot.ContainsKey(proc.Id))
                        {
                            try { _throttleSnapshot[proc.Id] = (proc.PriorityClass, proc.ProcessorAffinity); }
                            catch { /* protected/exited proc — can't read; skip snapshot */ }
                        }
                    }

                    // Gaming mode uses Idle to maximally yield CPU to the game.
                    // Any other mode uses BelowNormal to avoid starving background I/O.
                    var target = isGaming ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
                    if (proc.PriorityClass != target)
                    {
                        proc.PriorityClass = target;
                        throttled++;
                    }

                    // Affinity cage (gaming) / release (other modes). Best-effort: ProcessorAffinity
                    // throws for protected/exited procs — swallowed by the outer catch per process.
                    if (canCage && proc.ProcessorAffinity != affinityTarget)
                    {
                        proc.ProcessorAffinity = affinityTarget;
                        caged++;
                    }
                }
                catch { }
            }

            var targetName = isGaming ? "Idle" : "BelowNormal";
            var cageMsg = canCage
                ? (isGaming ? $", {caged} caged → cores 0-1" : $", {caged} released → all cores")
                : "";
            Logger.Log($"Smart throttle ({mode}): {throttled} processes → {targetName}{cageMsg}, {protected_} mode-relevant processes protected");

            // Mirror the snapshot to disk so a crash mid-throttle can't strand procs at Idle (#3b).
            PersistThrottleSnapshot();
        }

        /// <summary>
        /// Reverse <see cref="ThrottleForMode"/> EXACTLY: restore every process we throttled/caged
        /// back to the priority + CPU affinity it had BEFORE the mode touched it (from the
        /// snapshot), then clear the snapshot. Returns the count restored.
        ///
        /// THE FIX: this paired restore was missing from the game-exit / Restore-button path, which
        /// only un-boosted the handful of explicitly-tracked PIDs (RestoreProcessPriorities) and left
        /// the ~99 bulk-throttled background processes stranded at Idle — and caged to cores 0-1 — for
        /// the rest of the session, degrading the whole PC. Best-effort per process; never throws.
        /// </summary>
        public static int RestoreThrottledProcesses()
        {
            List<KeyValuePair<int, (ProcessPriorityClass prio, IntPtr affinity)>> snapshot;
            lock (_throttleSnapshotLock)
            {
                snapshot = new List<KeyValuePair<int, (ProcessPriorityClass, IntPtr)>>(_throttleSnapshot);
                _throttleSnapshot.Clear();
            }
            if (snapshot.Count == 0)
            {
                Logger.Log("RestoreThrottledProcesses: nothing throttled to restore");
                return 0;
            }
            int selfPid = Process.GetCurrentProcess().Id;
            int restored = 0;
            foreach (var kv in snapshot)
            {
                if (kv.Key == selfPid) continue;
                try
                {
                    using var p = Process.GetProcessById(kv.Key);
                    p.PriorityClass = kv.Value.prio;
                    // Restore original affinity too — this is what un-cages procs from cores 0-1.
                    if (kv.Value.affinity != IntPtr.Zero) p.ProcessorAffinity = kv.Value.affinity;
                    restored++;
                }
                catch { /* process exited or access denied — skip */ }
            }
            Logger.Log($"RestoreThrottledProcesses: restored {restored} of {snapshot.Count} throttled process(es) to original priority + affinity");
            // Normal restore done → the on-disk snapshot is no longer needed (#3b).
            DeleteThrottleSnapshotFile();
            return restored;
        }

        // ── #3b persistence helpers ──────────────────────────────────────────

        /// <summary>Write the current in-memory throttle snapshot to disk (PID + name + original priority +
        /// affinity) so a crash mid-throttle is recoverable on next launch. Best-effort; never throws.</summary>
        private static void PersistThrottleSnapshot()
        {
            try
            {
                List<ThrottleEntry> entries;
                lock (_throttleSnapshotLock)
                {
                    entries = new List<ThrottleEntry>(_throttleSnapshot.Count);
                    foreach (var kv in _throttleSnapshot)
                    {
                        // Capture the name now (proc is still alive, just throttled) to detect PID reuse on recovery.
                        string name = "";
                        try { using var p = Process.GetProcessById(kv.Key); name = p.ProcessName; } catch { }
                        entries.Add(new ThrottleEntry
                        {
                            Pid = kv.Key,
                            Name = name,
                            Priority = kv.Value.prio.ToString(),
                            Affinity = (long)kv.Value.affinity,
                        });
                    }
                }
                if (entries.Count == 0) { DeleteThrottleSnapshotFile(); return; }
                Directory.CreateDirectory(RegistryBackup.BackupDirectory);
                File.WriteAllText(_throttleSnapshotPath, JsonConvert.SerializeObject(entries));
                Logger.Log($"Throttle snapshot persisted ({entries.Count} procs) → {_throttleSnapshotPath} (crash-recoverable)");
            }
            catch (Exception ex) { Logger.Log("PersistThrottleSnapshot failed: " + ex.Message); }
        }

        private static void DeleteThrottleSnapshotFile()
        {
            try { if (File.Exists(_throttleSnapshotPath)) File.Delete(_throttleSnapshotPath); }
            catch (Exception ex) { Logger.Log("DeleteThrottleSnapshotFile failed: " + ex.Message); }
        }

        /// <summary>
        /// CRASH RECOVERY (#3b): if a throttle-snapshot file exists at startup, a PREVIOUS run was killed
        /// while Gaming Mode had procs throttled (the original incident — machine stranded at Idle).
        /// Restore each still-alive PID's original priority + affinity from the file (skipping any PID that
        /// has been REUSED by a different process since the crash — name guard), then delete the file.
        /// Call once on app startup. Best-effort; never throws. Returns the count restored.
        /// </summary>
        public static int RecoverThrottleSnapshotFromDisk()
        {
            try
            {
                if (!File.Exists(_throttleSnapshotPath)) return 0;
                var entries = JsonConvert.DeserializeObject<List<ThrottleEntry>>(File.ReadAllText(_throttleSnapshotPath))
                              ?? new List<ThrottleEntry>();
                int selfPid = Process.GetCurrentProcess().Id;
                int restored = 0, skippedReused = 0;
                foreach (var e in entries)
                {
                    if (e.Pid == selfPid) continue;
                    try
                    {
                        using var p = Process.GetProcessById(e.Pid);
                        // PID-reuse guard: if a different process now holds this PID, do NOT touch it.
                        if (!string.IsNullOrEmpty(e.Name) &&
                            !string.Equals(p.ProcessName, e.Name, StringComparison.OrdinalIgnoreCase))
                        { skippedReused++; continue; }

                        if (Enum.TryParse<ProcessPriorityClass>(e.Priority, out var prio))
                            p.PriorityClass = prio;
                        if (e.Affinity != 0) p.ProcessorAffinity = (IntPtr)e.Affinity;
                        restored++;
                    }
                    catch { /* PID gone or access denied — skip */ }
                }
                Logger.Log($"RecoverThrottleSnapshotFromDisk: previous run left a throttle snapshot — restored {restored} of {entries.Count} process(es) to original priority + affinity" +
                           (skippedReused > 0 ? $" ({skippedReused} skipped — PID reused)" : "") + " [crash-recovery]");
                DeleteThrottleSnapshotFile();
                return restored;
            }
            catch (Exception ex) { Logger.Log("RecoverThrottleSnapshotFromDisk failed: " + ex.Message); return 0; }
        }

        /// <summary>
        /// Generic throttle used when no specific mode is active (e.g. manual button press).
        /// </summary>
        public static void ThrottleBackgroundProcesses()
        {
            var systemProtect = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Registry", "Idle",
                "smss", "csrss", "wininit", "winlogon", "services", "lsass",
                "svchost", "dwm", "explorer", "conhost", "audiodg", "CoreCage",
            };
            int selfPid = Process.GetCurrentProcess().Id;
            int count   = 0;
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == selfPid) continue;
                    if (systemProtect.Contains(proc.ProcessName)) continue;
                    if (proc.PriorityClass != ProcessPriorityClass.BelowNormal)
                    {
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        count++;
                    }
                }
                catch { }
            }
            Logger.Log($"Generic throttle: {count} processes → BelowNormal");
        }
        
        /// <summary>
        /// Restores all settings to default.
        /// </summary>
        public static void RestoreAllSettings()
        {
            try
            {
                RestorePowerPlan();

                // Re-enable services: restore config AND start them so they run without a reboot
                RunCommand("sc config DiagTrack start= auto", ignoreErrors: true);
                RunCommand("sc start DiagTrack", ignoreErrors: true);

                RunCommand("sc config WSearch start= auto", ignoreErrors: true);
                RunCommand("sc start WSearch", ignoreErrors: true);

                RunCommand("sc config Spooler start= auto", ignoreErrors: true);
                RunCommand("sc start Spooler", ignoreErrors: true);

                RunCommand("sc start SysMain", ignoreErrors: true);

                // Restore registry
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", true);
                    key?.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
                }
                catch { }

                Logger.Log("All settings restored (services re-enabled and started)");
            }
            catch (Exception ex)
            {
                Logger.LogError("RestoreAllSettings failed", ex);
            }
        }
        
        private static void RunCommand(string args, bool ignoreErrors = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
            catch when (ignoreErrors) { }
        }
    }
}
