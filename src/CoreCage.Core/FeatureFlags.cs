using System;
using System.IO;
using Newtonsoft.Json;

namespace CoreCage.Core
{
    /// <summary>
    /// User-controllable switches for the powerful/native features, persisted to
    /// %LOCALAPPDATA%\CoreCage\features.json (edit + restart, or toggle in code). Safe + reversible
    /// features default ON; hardware-write features that need on-rig validation default OFF.
    /// </summary>
    public class FeatureFlags
    {
        // ── Safe + reversible → ON by default ────────────────────────────────────
        /// <summary>Measure Sleep(1) overshoot and apply the lowest-jitter timer resolution in Gaming Mode.</summary>
        public bool MeasuredTimerResolution { get; set; } = true;

        /// <summary>Watch the foreground window and auto-apply a matching game profile (no-op until profiles exist).</summary>
        public bool AutoApplyGameProfiles { get; set; } = true;

        // ── GPU core offset → OFF by default (Advanced opt-in) ───────────────────
        // Council 2026-06-01 (rank 1): default OFF for shipped/fresh installs. The native NVAPI
        // offset currently ships through the stale NvAPIWrapper.Net 0.8.1.101 pstates20 path
        // (do-not-ship per UPGRADES.md Tier 1.1) and SupportsClockOffset is only a _gpu!=null
        // presence check — no real capability/TDR probe yet (that's rank 6). Worst case is a
        // recoverable driver TDR, not a freeze, but an unvalidated offset on an unknown GPU is a
        // perceived-instability risk. Enable via the Auto Profiles "Advanced" toggle; an existing
        // features.json keeps the user's own validated value. Re-default ON only after rank 6.
        /// <summary>Apply a native NVAPI GPU core-clock offset in Gaming Mode (reset to 0 on Normal/Restore). Opt-in.</summary>
        public bool NativeGpuClockOffset { get; set; } = false;
        /// <summary>Core offset in MHz applied ONLY when NativeGpuClockOffset is enabled (this RTX 3060: +150 validated stable; memory OC is intentionally never touched).</summary>
        public int GpuCoreOffsetMhz { get; set; } = 150;

        // ── CPU Curve Optimizer → OFF by default (supervised, Advanced opt-in) ────
        // Council 2026-06-01 (rank 1): default OFF — this is the single highest hard-freeze/brick
        // vector in the codebase. A bad CO candidate requires a manual power-cycle, and the write
        // path is the patched ryzen-smu-cli at C:\tools\ryzen-smu-cli\ which bundles a SELF-CONTAINED
        // inpoutx64 (WinRing0-class) driver — NOT the signed PawnIO stack (PawnIO migration = future
        // rank). ryzenadj CO writes are also documented to crash 0xC0000005 on this Cezanne SMU. On a
        // GPU-bound, cool-running rig deeper CO buys ~zero felt FPS, so default-off costs almost
        // nothing. SUPERVISED ONLY — never re-enable for autonomous/overnight runs. Enable via the
        // Auto Profiles "Advanced" toggle; an existing features.json keeps the user's own value.
        /// <summary>
        /// Apply CPU Curve-Optimizer via the patched ryzen-smu-cli (self-contained inpoutx64 driver,
        /// SMU all-core --offset-all command). Validated: -5 applied + read back, reverted cleanly
        /// (see docs/CPU-CO-VALIDATION.md). Opt-in/supervised only.
        /// </summary>
        public bool NativeCpuCurveOptimizer { get; set; } = false;
        /// <summary>All-core CO offset (negative = undervolt) applied ONLY when NativeCpuCurveOptimizer is enabled. Conservative value; clamp band is [-30,0].</summary>
        public int CpuCurveOffset { get; set; } = -10;

        // ── CPU Thermal Guard → ON by default (safe: only reins in BACKGROUND hogs) ──
        /// <summary>Auto-protect the CPU: when temp crosses ThermalGuardHighC, confine the busiest
        /// BACKGROUND processes (never the foreground app/game, never the OS) to a few cores at Idle
        /// priority until temp falls back below ThermalGuardReleaseC. Prevents runaway batch jobs
        /// (e.g. transcoders/transcription swarms) from cooking the CPU. The hardware power-cap path
        /// (ryzenadj) faults on this Cezanne APU, so workload-throttling is the working lever.</summary>
        public bool CpuThermalGuard { get; set; } = true;
        /// <summary>Engage the guard at/above this CPU temperature (°C).</summary>
        public double ThermalGuardHighC { get; set; } = 88;
        /// <summary>Release the guard once CPU temp falls to/below this (°C). Hysteresis vs High.</summary>
        public double ThermalGuardReleaseC { get; set; } = 80;

        // ── Core Cage → ON by default (safe: user-mode affinity only, EAC-safe) ──
        /// <summary>The flagship feature: in Gaming Mode, reserve the top <see cref="CoreCageReservedCores"/>
        /// logical cores for the game and confine background processes onto the leftover (bottom) cores —
        /// the technique measured 77→~150fps in Arc Raiders. Pure planner (<c>CoreCageService.BuildPlan</c>)
        /// + a thin per-pid <c>ProcessorAffinity</c> applier; EAC-safe (user-mode APIs only, no kernel
        /// calls). Safe + reversible → ON by default.</summary>
        public bool CoreCageEnabled { get; set; } = true;

        /// <summary>How many logical cores to reserve for the game (the TOP cores; everything else is
        /// caged). Defaults to half the machine's logical core count, floored at 2 so a fresh install
        /// always leaves the game a meaningful reservation. <c>CoreCageService.BuildPlan</c> still
        /// refuses to cage anything on a ≤2-core machine regardless of this value.</summary>
        public int CoreCageReservedCores { get; set; } = DefaultCoreCageReservedCores();

        private static int DefaultCoreCageReservedCores() =>
            Math.Max(Environment.ProcessorCount / 2, 2);

        // ── persistence ──────────────────────────────────────────────────────────
        private static FeatureFlags? _current;
        public static FeatureFlags Current => _current ??= Load();

        private static readonly string PathJson = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "features.json");

        public static FeatureFlags Load()
        {
            try
            {
                if (File.Exists(PathJson))
                    return JsonConvert.DeserializeObject<FeatureFlags>(File.ReadAllText(PathJson)) ?? new FeatureFlags();
            }
            catch (Exception ex) { Logger.LogError("Loading features.json failed", ex); }

            var f = new FeatureFlags();
            f.Save(); // write defaults so there's a file to edit
            return f;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathJson)!);
                File.WriteAllText(PathJson, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { Logger.LogError("Saving features.json failed", ex); }
        }
    }
}
