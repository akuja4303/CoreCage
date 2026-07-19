using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace CoreCage.Core
{
    public static class PerformanceTuner
    {
        private static readonly string NvSmi = ResolveNvSmiPath();

        private static string ResolveNvSmiPath()
        {
            // File.Exists can throw in pathological environments (restricted ACLs, long-path
            // rules); a throw here would poison the whole class as an opaque
            // TypeInitializationException at first use, mid-mode-activation. Never throw.
            const string sys32 = @"C:\Windows\System32\nvidia-smi.exe";
            const string nvsmi = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
            try { return File.Exists(sys32) ? sys32 : nvsmi; }
            catch { return nvsmi; }
        }

        // ── GPU Power Limit ───────────────────────────────────────────────────

        public static void SetGpuPowerLimit(int watts)
        {
            // nvidia-smi only exists / works on NVIDIA. On AMD/Intel GPUs this previously spawned a
            // doomed process and logged a fake "→ NW" success; now we skip honestly.
            if (Hardware.HardwareProfile.Current.GpuVendor != Hardware.GpuVendor.Nvidia)
            {
                Logger.Log($"GPU power limit skipped — no NVIDIA GPU detected " +
                           $"({Hardware.HardwareProfile.Current.GpuName}); nvidia-smi path N/A.");
                return;
            }
            RunNvSmi($"-pl {watts}");
            Logger.Log($"GPU power limit → {watts}W");
        }

        /// <summary>Reads the GPU's (min, current, max) power limits in watts. Returns (0,0,0) when the
        /// real values can't be determined — non-NVIDIA GPU or a failed nvidia-smi read. Callers must
        /// treat max&lt;=min as "unknown" and keep their own defaults; we no longer return a 3060-shaped
        /// (100,170,170) guess that mis-clamps the slider on a 4090, an AMD card, or a laptop iGPU.</summary>
        public static (int min, int current, int max) GetGpuPowerLimits()
        {
            if (Hardware.HardwareProfile.Current.GpuVendor != Hardware.GpuVendor.Nvidia)
                return (0, 0, 0);   // no nvidia-smi to query; UI keeps defaults / disables the slider
            try
            {
                string raw = RunNvSmiCapture(
                    "--query-gpu=power.min_limit,power.limit,power.max_limit --format=csv,noheader,nounits");
                var parts = raw.Split(',');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0].Trim().Split('.')[0], out int min) &&
                    int.TryParse(parts[1].Trim().Split('.')[0], out int cur) &&
                    int.TryParse(parts[2].Trim().Split('.')[0], out int max))
                    return (min, cur, max);
            }
            catch { }
            Logger.Log("GPU power limits unreadable (nvidia-smi query failed) — slider left at defaults");
            return (0, 0, 0);
        }

        public static (int coreMhz, int memMhz, float powerW, float tempC) GetGpuStats()
        {
            try
            {
                string raw = RunNvSmiCapture(
                    "--query-gpu=clocks.gr,clocks.mem,power.draw,temperature.gpu --format=csv,noheader,nounits");
                var p = raw.Split(',');
                if (p.Length >= 4)
                    return (
                        int.TryParse(p[0].Trim(), out int c) ? c : 0,
                        int.TryParse(p[1].Trim(), out int m) ? m : 0,
                        float.TryParse(p[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float pw) ? pw : 0,
                        float.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float t) ? t : 0
                    );
            }
            catch { }
            return (0, 0, 0, 0);
        }

        // ── CPU Boost Mode ────────────────────────────────────────────────────
        // AMD Ryzen boost modes via powercfg processor performance boost policy
        // 0=Disabled, 1=Enabled, 2=Aggressive, 3=EfficientAggressive

        public static void SetCpuBoostMode(CpuBoostMode mode)
        {
            int val = (int)mode;

            // Unhide PERFBOOSTMODE so the subsequent set calls don't silently no-op.
            string unhideOut = RunPowercfgCapture("-attributes SUB_PROCESSOR PERFBOOSTMODE -ATTRIB_HIDE");
            if (!string.IsNullOrWhiteSpace(unhideOut))
                Logger.Log($"powercfg unhide PERFBOOSTMODE: {unhideOut.Trim()}");

            // Set both AC (plugged-in) and DC (battery) so the policy takes effect on all power states.
            string acOut = RunPowercfgCapture($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {val}");
            if (!string.IsNullOrWhiteSpace(acOut))
                Logger.Log($"powercfg setacvalueindex PERFBOOSTMODE={val}: {acOut.Trim()}");

            string dcOut = RunPowercfgCapture($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {val}");
            if (!string.IsNullOrWhiteSpace(dcOut))
                Logger.Log($"powercfg setdcvalueindex PERFBOOSTMODE={val}: {dcOut.Trim()}");

            // Activate the scheme so the new values are applied immediately.
            string activeOut = RunPowercfgCapture("/setactive SCHEME_CURRENT");
            if (!string.IsNullOrWhiteSpace(activeOut))
                Logger.Log($"powercfg setactive: {activeOut.Trim()}");

            Logger.Log($"CPU boost mode → {mode}");
        }

        // ── RyzenAdj — direct SMU power limit control ─────────────────────────
        // Ryzen 5 5600G (Cezanne) stock: STAPM=65W, fast PPT=88W, slow=65W
        // stapm/fast/slow in milliwatts, temp in °C

        private static readonly string RyzenAdj = @"C:\tools\ryzenadj\ryzenadj.exe";

        public static void SetRyzenPowerLimits(int staPmW, int fastW, int slowW, int tempC, int coAllOffset = 0, int tdcA = 0, int edcA = 0, TuningOutcome? outcome = null)
        {
            // Curve Optimizer FIRST and independently — it uses ryzen-smu-cli (its own ring-0 path), NOT
            // ryzenadj. It must not be gated on the ryzenadj power write below, which can fault 0xC0000005
            // when another SMU client (e.g. CapFrameX/PawnIO that Gaming Mode launches) holds the mailbox.
            // Gated + no-op unless the feature flag is on and the tool is present.
            if (coAllOffset != 0) ApplyCurveOptimizer(coAllOffset, outcome);

            if (!File.Exists(RyzenAdj))
            {
                Logger.Log("RyzenAdj not found — skipping CPU power limit");
                outcome?.Add("CPU power limits", "RyzenAdj.exe not found — CPU power limits NOT applied.", TuningSeverity.Warning);
                return;
            }
            // CO is excluded from the ryzenadj command (--set-coall faults 0xC0000005 on Cezanne); only the
            // power limits go through ryzenadj here.
            string args = TuningState.BuildRyzenAdjArgs(new CpuTuningValues
            {
                StapmW = staPmW, FastW = fastW, SlowW = slowW, TctlC = tempC,
                CoAll = 0, TdcA = tdcA, EdcA = edcA
            });
            // Run ryzenadj under the ecosystem-standard Global\Access_PCI mutex so the dominant concurrent
            // SMU reader — our OWN LibreHardwareMonitor 2s sensor poll, which honors this mutex — backs off
            // for the write window. ryzenadj/WinRing0 itself does NOT take the lock, so we also retry on the
            // 0xC0000005 fault that a non-cooperating reader (CapFrameX/PawnIO, Ryzen Master) can still cause.
            // SMU power-limit writes are idempotent, so retrying is safe. The mutex wraps ONLY ryzenadj — the
            // CO path above is a separate process (ryzen-smu-cli) that takes this same mutex itself, so nesting
            // it here would deadlock.  Root cause + sources: docs/CPU-CO-VALIDATION.md / RyzenAdj #138.
            const int Crash = unchecked((int)0xC0000005);   // -1073741819
            int exit = -1; string ryzenOut = "";
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                (exit, ryzenOut) = RunRyzenAdjLocked(args);
                if (exit == 0) break;
                Logger.Log($"RyzenAdj power write attempt {attempt}/4 failed (exit 0x{(uint)exit:X8})" +
                           (exit == Crash ? " — SMU/PCI contention; backing off" : "") + ".");
                Thread.Sleep(60 * attempt);
            }
            if (exit != 0)
            {
                Logger.LogError($"RyzenAdj WRITE FAILED after retries — exit 0x{(uint)exit:X8} ({exit}); power limits " +
                                $"NOT applied (STAPM={staPmW}W Fast={fastW}W Slow={slowW}W Temp={tempC}°C). " +
                                $"Close other SMU tools (CapFrameX / Ryzen Master) or reboot if it persists.");
                outcome?.Add("CPU power limits",
                    $"RyzenAdj write FAILED (exit 0x{(uint)exit:X8}) — power limits NOT applied. Close other SMU tools (CapFrameX / Ryzen Master) or reboot.",
                    TuningSeverity.Error);
                return;
            }
            Logger.Log($"RyzenAdj → STAPM={staPmW}W Fast={fastW}W Slow={slowW}W Temp={tempC}°C");

            // Per-parameter results parsed via tested TuningState.ParseRyzenAdjOutput.
            // ryzenadj exits 0 even when individual parameters are unsupported; failures appear in stdout.
            foreach (var r in TuningState.ParseRyzenAdjOutput(ryzenOut))
            {
                Logger.Log(r.Ok ? $"RyzenAdj OK: {r.Param}" : $"RyzenAdj WARN (param silently skipped): {r.Param}");
                if (!r.Ok)
                    outcome?.Add("CPU power", $"Parameter silently skipped by RyzenAdj: {r.Param}.", TuningSeverity.Warning);
            }
        }

        // Cached handle to the cross-process SMU/PCI-config mutex that LibreHardwareMonitor, ZenStates and
        // HWiNFO all serialize on. Open the existing object if a cooperating tool already created it, else
        // create it. Null only if the OS denies access (then we run unlocked + rely on retry).
        private static Mutex? _pciMutex;
        private static bool _pciMutexTried;
        private static Mutex? PciMutex()
        {
            if (_pciMutexTried) return _pciMutex;
            _pciMutexTried = true;
            try
            {
                if (Mutex.TryOpenExisting(@"Global\Access_PCI", out var existing)) _pciMutex = existing;
                else _pciMutex = new Mutex(false, @"Global\Access_PCI");
            }
            catch { try { _pciMutex = new Mutex(false, @"Global\Access_PCI"); } catch { _pciMutex = null; } }
            return _pciMutex;
        }

        // Invokes ryzenadj once while holding Global\Access_PCI so cooperating SMU readers pause for the
        // write. Returns (exitCode, stdout). Same thread does Wait + Release (mutex requires it).
        private static (int exit, string output) RunRyzenAdjLocked(string args)
        {
            Mutex? pci = PciMutex();
            bool held = false;
            try
            {
                if (pci != null)
                {
                    try { held = pci.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (AbandonedMutexException) { held = true; }   // prior holder crashed — safe to take
                }
                var psi = new ProcessStartInfo(RyzenAdj, args)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                string outp = p?.StandardOutput.ReadToEnd() ?? "";   // read before WaitForExit (pipe-buffer deadlock)
                p?.WaitForExit(5000);
                int exit = -1;
                try { if (p != null && p.HasExited) exit = p.ExitCode; } catch { }
                return (exit, outp);
            }
            catch (Exception ex)
            {
                Logger.LogError("RunRyzenAdjLocked failed", ex);
                return (-1, "");
            }
            finally
            {
                if (held) { try { pci!.ReleaseMutex(); } catch { } }
            }
        }

        public static string GetRyzenInfo()
        {
            if (!File.Exists(RyzenAdj)) return "RyzenAdj not found";
            var psi = new ProcessStartInfo(RyzenAdj, "--info")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(5000);
            return output.Trim();
        }

        // ── Presets ───────────────────────────────────────────────────────────

        public static TuningOutcome ApplyGamingPreset(int gpuMaxWatts)
        {
            var outcome = new TuningOutcome();
            if (Hardware.HardwareProfile.Current.GpuVendor != Hardware.GpuVendor.Nvidia)
                outcome.Add("GPU power limit",
                    $"Skipped — no NVIDIA GPU detected ({Hardware.HardwareProfile.Current.GpuName}). " +
                    "GPU power tuning needs nvidia-smi/NVAPI; CPU + system tweaks still applied.",
                    TuningSeverity.Info);
            SetGpuPowerLimit(gpuMaxWatts);
            SetCpuBoostMode(CpuBoostMode.Aggressive);
            // Max sustained power — 95W STAPM (above stock 88W APU cap), 90°C target. CO (-10, conservative)
            // is applied via the all-core smu-cli path inside SetRyzenPowerLimits (gated by the feature flag).
            SetRyzenPowerLimits(staPmW: 95, fastW: 105, slowW: 95, tempC: 90, coAllOffset: FeatureFlags.Current.CpuCurveOffset, tdcA: 75, edcA: 110, outcome: outcome);
            Logger.Log("Performance Tuner: Gaming preset applied");
            return outcome;
        }

        public static TuningOutcome ApplyNormalPreset(int gpuMaxWatts)
        {
            var outcome = new TuningOutcome();
            SetGpuPowerLimit(gpuMaxWatts);
            SetCpuBoostMode(CpuBoostMode.Enabled);
            // Stock behavior — STAPM=65W, fast=88W, slow=65W, 85°C
            SetRyzenPowerLimits(staPmW: 65, fastW: 88, slowW: 65, tempC: 85, outcome: outcome);
            ResetCurveOptimizer(outcome);   // back to stock CO=0 (Normal preset is also the Restore path)
            Logger.Log("Performance Tuner: Normal preset applied");
            return outcome;
        }

        // ── Curve Optimizer via the Cezanne-safe SMU path ────────────────────────
        // ryzenadj --set-coall faults 0xC0000005 on this 5600G (SMU rejects it), so CO is applied
        // through ryzen-smu-cli (ZenStates-Core + PawnIO) instead — but ONLY when explicitly enabled,
        // because an unvalidated CO value can hard-freeze the rig and the CLI flags are not yet
        // hardware-confirmed. Gated by FeatureFlags.NativeCpuCurveOptimizer (default OFF) + tool presence.
        private const int CoreCount5600G = 6;
        // Apply CO to every PHYSICAL core on the actual chip (not a hardcoded 6) so an 8-core Ryzen
        // doesn't leave 2 cores at stock voltage. Falls back to the old constant if detection fails.
        private static int CoreCount => Hardware.HardwareProfile.Current.PhysicalCores > 0
            ? Hardware.HardwareProfile.Current.PhysicalCores : CoreCount5600G;

        /// <summary>Reset Curve Optimizer to 0 (stock) on all cores. Called by the Normal preset / Restore
        /// so a restore truly returns to stock (CO is otherwise sticky until reboot).</summary>
        private static void ResetCurveOptimizer(TuningOutcome? outcome = null)
        {
            if (!FeatureFlags.Current.NativeCpuCurveOptimizer) return;
            var smu = new RyzenSmuCliController();
            if (!smu.IsAvailable) return;
            SmuApplyResult r = smu.ApplyAllCoreOffset(0, CoreCount);
            Logger.Log($"Curve Optimizer reset to 0 (stock): ok={r.Ok} verified={r.Verified}");
            if (!r.Ok || !r.Verified)
                outcome?.Add("CPU Curve Optimizer",
                    $"CO reset to stock may NOT have applied (ok={r.Ok}, verified={r.Verified}) — undervolt could still be active until reboot.",
                    TuningSeverity.Warning);
        }

        private static void ApplyCurveOptimizer(int offset, TuningOutcome? outcome = null)
        {
            if (offset == 0) return;

            bool flag = FeatureFlags.Current.NativeCpuCurveOptimizer;
            if (!flag)
            {
                // Intentionally off (the new safe default) — not a user-facing warning; power limits still apply.
                Logger.Log($"Curve Optimizer offset {offset} requested but FeatureFlags.NativeCpuCurveOptimizer is OFF — " +
                           "skipped (power limits still applied). Validate on-rig, then enable the flag.");
                return;
            }

            var smu = new RyzenSmuCliController();
            bool available = smu.IsAvailable;
            bool ok = false, verified = false;
            if (available)
            {
                SmuApplyResult r = smu.ApplyAllCoreOffset(offset, CoreCount);
                ok = r.Ok; verified = r.Verified;
                Logger.Log($"Curve Optimizer via SMU: ok={r.Ok} verified={r.Verified} — {r.Message}");
            }
            else
            {
                Logger.Log("Curve Optimizer enabled but ryzen-smu-cli.exe not found — install ryzen-smu-cli + PawnIO first");
            }

            // Surface a silent no-op (the council rank-4 fix): when CO is ON but the SMU rejected/failed
            // the write — or the tool is missing — the UI must not keep claiming the tune is active.
            var warn = ClassifyCurveOptimizer(flag, available, offset, ok, verified);
            if (warn != null) outcome?.Add(warn);
        }

        /// <summary>
        /// Pure decision: does this Curve-Optimizer apply deserve a user-facing warning? Returns
        /// <c>null</c> when there is nothing to surface (offset 0, intentionally disabled, or a clean
        /// verified apply); a <see cref="TuningWarning"/> when CO is enabled but did not actually land.
        /// </summary>
        public static TuningWarning? ClassifyCurveOptimizer(bool flagEnabled, bool toolAvailable, int offset, bool ok, bool verified)
        {
            if (offset == 0) return null;
            if (!flagEnabled) return null; // off by design — covered by the log, not a warning
            if (!toolAvailable)
                return new TuningWarning("CPU Curve Optimizer",
                    $"CO {offset} is enabled but ryzen-smu-cli.exe was not found — install it + PawnIO. CO is NOT active.",
                    TuningSeverity.Warning);
            if (!ok || !verified)
                return new TuningWarning("CPU Curve Optimizer",
                    $"CO {offset} did NOT apply — the SMU silently rejected/failed the write (ok={ok}, verified={verified}). Tuning is NOT active.",
                    TuningSeverity.Warning);
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void RunNvSmi(string args)
        {
            if (!File.Exists(NvSmi)) return;
            var psi = new ProcessStartInfo(NvSmi, args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }

        private static string RunNvSmiCapture(string args)
        {
            if (!File.Exists(NvSmi)) return "";
            var psi = new ProcessStartInfo(NvSmi, args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(5000);
            return output.Trim();
        }

        private static void RunPowercfg(string args)
        {
            var psi = new ProcessStartInfo("powercfg", args)
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }

        private static string RunPowercfgCapture(string args)
        {
            var psi = new ProcessStartInfo("powercfg", args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            // Read before WaitForExit to avoid pipe-buffer deadlock.
            string output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);
            return output.Trim();
        }
    }

    public enum CpuBoostMode
    {
        Disabled            = 0,
        Enabled             = 1,
        Aggressive          = 2,
        EfficientAggressive = 3,
    }
}
