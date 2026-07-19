using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace CoreCage.Core
{
    /// <summary>
    /// Anti-cheat-safe game performance boost via Windows-native mechanisms
    /// (IFEO + per-exe Power Throttling) that don't tamper with the running
    /// process. Required because runtime SetPriorityClass() is blocked by
    /// EAC / BattlEye / Vanguard / etc. for protected game processes.
    ///
    /// Strategy:
    ///   (1) IFEO CpuPriorityClass — game is BORN with High priority. The
    ///       anti-cheat sees a normal process launch; no runtime tampering.
    ///       Persists across reboots until cleared.
    ///   (2) IFEO CpuPriorityBoost — disables OS auto-boost so the priority
    ///       we set is the one that sticks (no "now Normal again" drift).
    ///   (3) powercfg /powerthrottling disable /path EXE — tells Windows
    ///       NEVER to throttle this exe under EcoQoS. Official Windows API.
    ///   (4) IFEO MitigationOptions clears Spectre-style mitigations for
    ///       this exe (optional, off by default — small frametime gain at
    ///       a security cost; require explicit opt-in).
    /// </summary>
    public static class EacSafePriority
    {
        // Image File Execution Options root — by EXE NAME, not full path.
        // Anti-cheats can't touch this because it applies at process creation.
        private const string IFEO_ROOT =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        // PerfOptions CpuPriorityClass values per Windows API documentation:
        //   1 = Idle, 2 = Normal, 3 = High, 5 = Realtime, 6 = BelowNormal, 8 = AboveNormal
        private const int CPU_PRIORITY_HIGH       = 3;
        private const int CPU_PRIORITY_ABOVENORMAL = 8;
        private const int CPU_PRIORITY_NORMAL     = 2;

        /// <summary>
        /// Set High priority + (optional) affinity mask at every launch of exeName.
        /// affinityMask=0 leaves affinity unset (Windows default = all cores).
        /// On Ryzen 6c/12t, mask 0xFFC pins to cores 2-11 (leaves 0-1 for OS interrupts).
        /// Persists across reboots until cleared.
        /// </summary>
        public static bool ApplyPreLaunchHighPriority(string exeName, long affinityMask = 0)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return false;
            try
            {
                var path = $@"{IFEO_ROOT}\{exeName}\PerfOptions";
                using (var key = Registry.LocalMachine.CreateSubKey(path, writable: true))
                {
                    if (key == null) { Logger.Log($"EacSafePriority: could not create IFEO key for {exeName}"); return false; }
                    key.SetValue("CpuPriorityClass", CPU_PRIORITY_HIGH, RegistryValueKind.DWord);
                    // CpuPriorityBoost = 0 stops the OS from drifting our priority back to Normal
                    // during long-running compute (a known frametime drift on Ryzen + UE5).
                    key.SetValue("CpuPriorityBoost", 0, RegistryValueKind.DWord);
                    // IoPriority 3 = High — game's I/O (texture streaming, shader cache) jumps queues.
                    key.SetValue("IoPriority", 3, RegistryValueKind.DWord);
                    if (affinityMask != 0)
                    {
                        // CpuAffinityMask is a QWord (64-bit) for >32-core support.
                        key.SetValue("CpuAffinityMask", affinityMask, RegistryValueKind.QWord);
                    }
                }
                var affMsg = affinityMask != 0 ? $" + affinity 0x{affinityMask:X}" : "";
                Logger.Log($"EacSafePriority: IFEO High priority{affMsg} + IoPriority High armed for {exeName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"EacSafePriority.ApplyPreLaunchHighPriority({exeName}) failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Disable Windows Fullscreen Optimizations for the exe. Per-user (HKCU),
        /// keyed by full path. Recommended by the rig-playbook for ARC Raiders
        /// (true exclusive fullscreen = lowest input latency, no DWM compositor).
        /// Returns true if the value was written.
        /// </summary>
        public static bool DisableFullscreenOptimizations(string fullExePath)
        {
            if (string.IsNullOrWhiteSpace(fullExePath)) return false;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
                if (key == null) return false;
                // Value name = full path to exe; data = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE"
                // The leading "~ " is required by the AppCompat schema.
                key.SetValue(fullExePath, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE", RegistryValueKind.String);
                Logger.Log($"EacSafePriority: fullscreen optimizations OFF for {System.IO.Path.GetFileName(fullExePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"EacSafePriority.DisableFullscreenOptimizations failed", ex);
                return false;
            }
        }

        /// <summary>Restore default fullscreen-optimizations behaviour (deletes the per-exe layer entry).</summary>
        public static bool RestoreFullscreenOptimizations(string fullExePath)
        {
            if (string.IsNullOrWhiteSpace(fullExePath)) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
                if (key == null) return true;
                key.DeleteValue(fullExePath, throwOnMissingValue: false);
                return true;
            }
            catch (Exception ex) { Logger.LogError("RestoreFullscreenOptimizations failed", ex); return false; }
        }

        /// <summary>
        /// Pause background services that cause frametime micro-stutters:
        ///   - wuauserv (Windows Update) — grabs CPU+disk for telemetry/scan/download bursts
        ///   - SysMain (Superfetch)       — reads disks predictively, spikes I/O queue
        ///   - WSearch (Windows Search)   — indexer rebuilds during idle, never truly idle
        /// All restartable; restored on Restore. Anti-cheat-safe (system services, not game).
        /// </summary>
        public static int PauseBackgroundServicesDuringGaming()
        {
            string[] services = { "wuauserv", "SysMain", "WSearch" };
            int paused = 0;
            foreach (var svc in services)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "sc.exe",
                        Arguments              = $"stop {svc}",
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(4000); if (p.ExitCode == 0 || p.ExitCode == 1062 /* not running */) paused++; }
                }
                catch (Exception ex) { Logger.Log($"EacSafePriority.Pause({svc}) skipped: {ex.Message}"); }
            }
            Logger.Log($"EacSafePriority: paused {paused}/{services.Length} stutter-source services for gaming session");
            return paused;
        }

        /// <summary>Restart the services that PauseBackgroundServicesDuringGaming stopped.</summary>
        public static int ResumeBackgroundServicesAfterGaming()
        {
            string[] services = { "wuauserv", "SysMain", "WSearch" };
            int resumed = 0;
            foreach (var svc in services)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "sc.exe",
                        Arguments              = $"start {svc}",
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(4000); if (p.ExitCode == 0 || p.ExitCode == 1056 /* already running */) resumed++; }
                }
                catch { }
            }
            Logger.Log($"EacSafePriority: resumed {resumed}/{services.Length} services after gaming");
            return resumed;
        }

        /// <summary>
        /// Increase GPU TdrDelay (Timeout Detection and Recovery) from 2s default to 10s.
        /// On UE5 + RTX, brief shader compilation can exceed 2s on a single frame, causing
        /// the driver to reset the GPU mid-frame — appears as a massive frametime spike.
        /// 10s lets compute survive without false-positive resets. Requires a reboot to apply.
        /// </summary>
        public static bool SetTdrDelay(int seconds = 10)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
                if (key == null) return false;
                key.SetValue("TdrDelay", seconds, RegistryValueKind.DWord);
                key.SetValue("TdrDdiDelay", seconds, RegistryValueKind.DWord);
                Logger.Log($"EacSafePriority: TdrDelay set to {seconds}s (was 2s default). REBOOT required to apply.");
                return true;
            }
            catch (Exception ex) { Logger.LogError("SetTdrDelay failed", ex); return false; }
        }

        /// <summary>
        /// Remove the IFEO key so the exe launches at default priority again.
        /// Idempotent — no-op if no key exists.
        /// </summary>
        public static bool RestoreDefaultLaunchPriority(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return false;
            try
            {
                var path = $@"{IFEO_ROOT}\{exeName}";
                using (var parent = Registry.LocalMachine.OpenSubKey(path, writable: true))
                {
                    if (parent == null) return true; // nothing to undo
                    parent.DeleteSubKey("PerfOptions", throwOnMissingSubKey: false);
                }
                // If the IFEO\<exeName> key is now empty (no other subkeys/values), remove it entirely.
                using (var probe = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (probe != null && probe.SubKeyCount == 0 && probe.ValueCount == 0)
                    {
                        Registry.LocalMachine.DeleteSubKey(path, throwOnMissingSubKey: false);
                    }
                }
                Logger.Log($"EacSafePriority: IFEO cleared for {exeName} (default priority restored on next launch)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"EacSafePriority.RestoreDefaultLaunchPriority({exeName}) failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Use powercfg to disable Windows Power Throttling for a specific exe path.
        /// This tells the OS to NEVER apply EcoQoS to this process. Official Windows API,
        /// anti-cheats are aware of and ignore it.
        /// Requires the FULL PATH to the exe.
        /// </summary>
        public static bool DisablePowerThrottling(string fullExePath)
        {
            if (string.IsNullOrWhiteSpace(fullExePath)) return false;
            return RunPowercfg($"/powerthrottling disable /path \"{fullExePath}\"", fullExePath);
        }

        /// <summary>Restore the default power-throttling behaviour for the exe.</summary>
        public static bool RestoreDefaultPowerThrottling(string fullExePath)
        {
            if (string.IsNullOrWhiteSpace(fullExePath)) return false;
            return RunPowercfg($"/powerthrottling reset /path \"{fullExePath}\"", fullExePath);
        }

        private static bool RunPowercfg(string args, string label)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "powercfg.exe",
                    Arguments              = args,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var p = Process.Start(psi);
                if (p == null) { Logger.Log($"EacSafePriority.RunPowercfg: failed to start powercfg ({args})"); return false; }
                p.WaitForExit(5000);
                if (p.ExitCode == 0)
                {
                    Logger.Log($"EacSafePriority: powercfg {args} OK for {label}");
                    return true;
                }
                var err = p.StandardError.ReadToEnd();
                Logger.Log($"EacSafePriority: powercfg {args} exit {p.ExitCode}, stderr: {err.Trim()}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"EacSafePriority.RunPowercfg({args}) failed", ex);
                return false;
            }
        }

        /// <summary>
        /// One-shot polish: for every gaming exe arm
        ///   (a) IFEO CpuPriorityClass = High + IoPriority = High + CpuPriorityBoost = 0
        ///   (b) IFEO CpuAffinityMask  = non-OS cores (auto-computed for this host)
        ///   (c) powercfg per-exe Power Throttling OFF (if process is currently running)
        ///   (d) Per-exe Fullscreen Optimizations OFF (if process is running, for full path)
        /// All operations are official Windows APIs; anti-cheats see no runtime tampering.
        /// Returns count of exes that got their IFEO key written.
        /// </summary>
        public static int ApplyPolishToGamingList(IEnumerable<string> exeNames)
        {
            // Compute non-OS core affinity once. Leave cores 0+1 for the OS interrupts,
            // give the game everything else. On a Ryzen 6c/12t this is mask 0xFFC (cores 2-11).
            int totalCores = Environment.ProcessorCount;
            long nonOsMask = 0;
            for (int i = 2; i < totalCores; i++) nonOsMask |= (1L << i);
            if (nonOsMask == 0) nonOsMask = (1L << totalCores) - 1; // fallback (shouldn't hit)

            int count = 0;
            foreach (var name in exeNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                if (ApplyPreLaunchHighPriority(fileName, nonOsMask)) count++;

                // Best-effort live-process tweaks (powercfg + FSO both need the real path).
                try
                {
                    var nameOnly = fileName.Substring(0, fileName.Length - 4);
                    foreach (var p in Process.GetProcessesByName(nameOnly))
                    {
                        try
                        {
                            var fullPath = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(fullPath))
                            {
                                DisablePowerThrottling(fullPath);
                                DisableFullscreenOptimizations(fullPath);
                            }
                        }
                        catch { /* MainModule can throw for protected procs — ignore */ }
                        finally { p.Dispose(); }
                    }
                }
                catch { /* swallow — IFEO write alone is the primary win */ }
            }
            return count;
        }

        /// <summary>Reverse of ApplyPolishToGamingList — clears IFEO + powercfg + FSO.</summary>
        public static int RestorePolishFromGamingList(IEnumerable<string> exeNames)
        {
            int count = 0;
            foreach (var name in exeNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                if (RestoreDefaultLaunchPriority(fileName)) count++;
                try
                {
                    var nameOnly = fileName.Substring(0, fileName.Length - 4);
                    foreach (var p in Process.GetProcessesByName(nameOnly))
                    {
                        try
                        {
                            var fullPath = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(fullPath))
                            {
                                RestoreDefaultPowerThrottling(fullPath);
                                RestoreFullscreenOptimizations(fullPath);
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }
            return count;
        }
    }
}
