using System;
using System.Collections.Generic;
using CoreCage.Core;

namespace CoreCage.Core.Detection
{
    /// <summary>
    /// The POLICY ENGINE that maps a detected <see cref="ActivityMode"/> (from
    /// <see cref="ConfidenceClassifier"/>) onto the app's concrete tweak actions by
    /// REUSING the existing pipelines — it owns no low-level registry/powercfg logic
    /// of its own. Each mode is expressed purely as a sequence of calls into the
    /// already-tested engine primitives:
    ///
    ///   • Gaming  → the full <see cref="GamingModePlusPlus"/> latency layer
    ///               (+ EAC-safe per-exe polish, core-unpark, 0.5ms timer, gaming throttle).
    ///   • Coding  → a NEW, conservative policy built from existing primitives: a moderate
    ///               (High, no affinity cage) priority boost for the editor/compiler, core-unpark
    ///               ON, timer resolution ON — but it deliberately does NOT kill background dev
    ///               tools and does NOT apply the gaming NIC/QoS/MSI layer (builds need their
    ///               background tools responsive, not nuked).
    ///   • Normal  → restore to baseline via <see cref="RestoreEverything"/>.
    ///
    /// <see cref="DescribeActions"/> exposes the per-mode rule list as plain strings so the
    /// UI can render an "active rules" panel without re-deriving the policy.
    /// </summary>
    public sealed class ModePolicy
    {
        // Editor / compiler / build-tool executables that Coding Mode gives a moderate,
        // EAC-safe (IFEO pre-launch) High-priority boost. No affinity cage — coding wants
        // the whole machine responsive, not a competitive single-game core carve-out.
        // TODO(wire): source this from user settings / UserProcessLists once a coding list
        // exists; this conservative default covers the common .NET + VS Code toolchain.
        private static readonly string[] CodingBoostExes =
        {
            "devenv.exe",     // Visual Studio
            "Code.exe",       // VS Code
            "rider64.exe",    // JetBrains Rider
            "MSBuild.exe",    // build engine
            "dotnet.exe",     // SDK / build / test host
            "VBCSCompiler.exe", // Roslyn compiler server
            "csc.exe",        // C# compiler
            "node.exe",       // JS/TS toolchain
        };

        /// <summary>
        /// Apply the tweak policy for <paramref name="mode"/> by invoking the matching
        /// existing pipeline. Best-effort and non-throwing per the engine's conventions —
        /// each underlying call already swallows + logs its own failures.
        /// </summary>
        public void Apply(ActivityMode mode)
        {
            Logger.Log($"ModePolicy: applying policy for {mode} mode.");
            switch (mode)
            {
                case ActivityMode.Gaming:
                    ApplyGaming();
                    break;
                case ActivityMode.Coding:
                    ApplyCoding();
                    break;
                case ActivityMode.Normal:
                default:
                    // Normal == baseline: undo everything we ever applied.
                    Restore();
                    break;
            }
        }

        /// <summary>Full restore to the system's baseline via the existing Big-Red-Button pipeline.</summary>
        public void Restore()
        {
            Logger.Log("ModePolicy: restoring to baseline (RestoreEverything).");
            RestoreEverything.RestoreAll();
        }

        // ── Gaming ────────────────────────────────────────────────────────────
        // Reuse the full gaming stack. Nothing new here — just orchestration.
        private static void ApplyGaming()
        {
            // 1. EAC-safe per-exe polish (IFEO High + affinity cage + powercfg + FSO).
            //    TODO(wire): feed the real user gaming list; ApplyAll below already covers
            //    the system-level gaming layer, so an empty list here is safe.
            EacSafePriority.PauseBackgroundServicesDuringGaming();
            // 2. The Gaming Mode++ latency layer (MSI + NIC harden + GameDVR + BG apps + QoS).
            GamingModePlusPlus.ApplyAll();
            // 3. Power/scheduler primitives.
            SystemTweaks.ApplyHighPerformancePowerPlan();
            CoreUnpark.ApplyAll();
            SystemTweaks.SetTimerResolution(high: true);   // 0.5 ms
            // 4. Aggressive background throttle + core cage for the foreground game.
            SystemTweaks.ThrottleForMode("gaming");
        }

        // ── Coding (the only genuinely NEW policy) ──────────────────────────────
        // Built entirely from existing primitives, used conservatively:
        //   • EacSafePriority.ApplyPreLaunchHighPriority — moderate High-priority boost for the
        //     editor/compiler, affinityMask=0 (NO core cage; coding wants the whole CPU available).
        //   • CoreUnpark.ApplyAll — unpark cores + raise the perf floor (cuts wake latency, helps
        //     incremental builds spin up cores instantly).
        //   • SystemTweaks.SetTimerResolution(high) — tighter timer for a snappier editor/REPL.
        // Deliberately OMITTED vs Gaming:
        //   • NO ThrottleForMode — we must NOT bury background dev tools (test hosts, language
        //     servers, watchers, containers) at Idle/BelowNormal; builds depend on them.
        //   • NO GamingModePlusPlus (MSI/NIC/QoS/GameDVR) — irrelevant + invasive for coding.
        //   • NO high-performance power-plan swap — left at the user's plan so laptops/battery and
        //     fan noise stay sane during long editing sessions (core-unpark alone gives the
        //     responsiveness win without forcing the High Performance scheme).
        private static void ApplyCoding()
        {
            // Moderate, EAC-safe (pre-launch IFEO) priority boost — no affinity cage.
            int boosted = 0;
            foreach (var exe in CodingBoostExes)
            {
                if (EacSafePriority.ApplyPreLaunchHighPriority(exe, affinityMask: 0))
                    boosted++;
            }
            Logger.Log($"ModePolicy(Coding): pre-launch High priority armed for {boosted}/{CodingBoostExes.Length} dev exe(s) (no core cage).");

            CoreUnpark.ApplyAll();
            SystemTweaks.SetTimerResolution(high: true);
            Logger.Log("ModePolicy(Coding): core-unpark ON, timer resolution ON, background dev tools left untouched.");
        }

        /// <summary>
        /// The human-readable "active rules" each mode applies, as data for the UI.
        /// Order is presentation order (boosted apps, suspended apps, power plan, core-unpark,
        /// timer, DND). Pure — performs no system calls.
        /// </summary>
        public IReadOnlyList<string> DescribeActions(ActivityMode mode) => mode switch
        {
            ActivityMode.Gaming => new[]
            {
                "Boosted: foreground game (EAC-safe IFEO High priority + non-OS core affinity, per-exe FSO + power-throttling off)",
                "Suspended: background processes → Idle and caged to cores 0-1; wuauserv / SysMain / WSearch paused; background UWP apps disabled",
                "Power plan: High Performance",
                "Core-unpark: ON (all cores unparked, 100% min-perf floor)",
                "Timer resolution: 0.5 ms",
                "DND / latency: GameDVR capture killed, NICs hardened (EEE/flow-control off, buffers up), MSI mode on GPU+NIC, QoS DSCP for the game",
            },
            ActivityMode.Coding => new[]
            {
                "Boosted: editor / compiler / build tools (VS, VS Code, Rider, MSBuild, dotnet, Roslyn, node) — EAC-safe IFEO High priority, NO core cage",
                "Suspended: NONE — background dev tools (test hosts, language servers, watchers) left at normal priority on purpose",
                "Power plan: unchanged (user's current plan kept; core-unpark provides the responsiveness)",
                "Core-unpark: ON (all cores unparked, 100% min-perf floor)",
                "Timer resolution: 0.5 ms",
                "DND / network: NONE (no gaming NIC/QoS/MSI; builds need background tools responsive)",
            },
            // Normal
            _ => new[]
            {
                "Boosted: none",
                "Suspended: none — all throttled / caged processes released",
                "Power plan: Balanced (baseline)",
                "Core-unpark: restored to user's original",
                "Timer resolution: default",
                "DND / network: all CoreCage tweaks reversed (Big Red Button)",
            },
        };
    }
}
