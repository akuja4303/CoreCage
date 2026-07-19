using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CoreCage.Core.Caging
{
    /// <summary>
    /// Core Cage: CoreCage's flagship feature. Reserves the top logical cores for the game and confines
    /// everything else onto the leftover (bottom) cores — the technique measured 77→~150fps / 1% lows
    /// 48→85 in Arc Raiders (<c>arc-cage.ps1</c>). Mirrors <c>EacSafePriority</c>'s conventions: only
    /// user-mode APIs, no kernel calls, so it stays EAC-safe.
    ///
    /// Split into a PURE planner (<see cref="BuildPlan"/> — no Process/OS dependency, fully unit-tested)
    /// and a thin, impure applier (<see cref="Apply"/>/<see cref="Release"/> — real
    /// <c>Process.GetProcessById(pid).ProcessorAffinity</c> writes, one try/catch per pid so a single
    /// access-denied system process can never abort the rest). The applier is intentionally NOT
    /// exercised by any unit test — its real behavior is verified live in Task 11, same pattern as
    /// GamingMode's pipeline steps.
    /// </summary>
    public static class CoreCageService
    {
        /// <summary>
        /// Pure planner: given the machine's logical core count, how many of the TOP cores to reserve
        /// for the game, every currently-running (pid, name) pair, and a whitelist of process names that
        /// must never be caged (the game's own exe, "audiodg", and any user whitelist entries) — decide
        /// which cores go where and which pids to confine.
        ///
        /// Refuses to cage anything on a ≤2-core machine (nowhere meaningful to cage a background
        /// process to without starving it) — the masks are still computed, but <c>CagedPids</c> comes
        /// back empty.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="totalCores"/> is not positive, or <paramref name="reservedForGame"/> is
        /// negative or leaves no core at all for the cage (i.e. &gt;= <paramref name="totalCores"/>).
        /// </exception>
        public static CagePlan BuildPlan(
            int totalCores,
            int reservedForGame,
            IReadOnlyList<(int Pid, string Name)> processes,
            ISet<string> whitelist)
        {
            if (totalCores <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalCores), totalCores, "totalCores must be positive.");
            if (reservedForGame < 0)
                throw new ArgumentOutOfRangeException(nameof(reservedForGame), reservedForGame, "reservedForGame cannot be negative.");
            if (reservedForGame >= totalCores)
                throw new ArgumentOutOfRangeException(nameof(reservedForGame), reservedForGame,
                    "reservedForGame must leave at least one core free for the cage (must be < totalCores).");

            // Bottom cores [0 .. cagedCoreCount) are the cage; top cores [cagedCoreCount .. totalCores)
            // are reserved for the game — mirrors arc-cage.ps1 (background -> low cores, game keeps high cores).
            int cagedCoreCount = totalCores - reservedForGame;

            long gameMask = 0;
            for (int i = cagedCoreCount; i < totalCores; i++) gameMask |= 1L << i;

            long cagedMask = 0;
            for (int i = 0; i < cagedCoreCount; i++) cagedMask |= 1L << i;

            var cagedPids = new List<int>();

            // ≤2 logical cores: caging would confine background work to a single starved core (or
            // zero) for essentially no gain — refuse rather than pretend it's safe.
            if (totalCores > 2 && processes != null)
            {
                foreach (var (pid, name) in processes)
                {
                    if (IsWhitelisted(name, whitelist)) continue;
                    cagedPids.Add(pid);
                }
            }

            return new CagePlan(gameMask, cagedMask, cagedPids);
        }

        /// <summary>Sets <c>ProcessorAffinity</c> to <see cref="CagePlan.CagedMask"/> for every pid in
        /// the plan. One try/catch per pid — a process that's exited or denies access (system/protected
        /// processes) is silently skipped, not counted. Returns the number actually changed.</summary>
        public static int Apply(CagePlan plan)
        {
            if (plan?.CagedPids == null || plan.CagedPids.Count == 0) return 0;

            var mask = (IntPtr)plan.CagedMask;
            int changed = 0;
            foreach (int pid in plan.CagedPids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.ProcessorAffinity = mask;
                    changed++;
                }
                catch (Exception ex)
                {
                    Logger.Log($"CoreCageService.Apply: pid {pid} skipped ({ex.GetType().Name}: {ex.Message})");
                }
            }
            return changed;
        }

        /// <summary>Restores full-mask (<c>GameMask | CagedMask</c> — every core the plan covers)
        /// affinity for every pid in the plan. One try/catch per pid, same semantics as
        /// <see cref="Apply"/>. Returns the number actually restored.</summary>
        public static int Release(CagePlan plan)
        {
            if (plan?.CagedPids == null || plan.CagedPids.Count == 0) return 0;

            var fullMask = (IntPtr)(plan.GameMask | plan.CagedMask);
            int restored = 0;
            foreach (int pid in plan.CagedPids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.ProcessorAffinity = fullMask;
                    restored++;
                }
                catch (Exception ex)
                {
                    Logger.Log($"CoreCageService.Release: pid {pid} skipped ({ex.GetType().Name}: {ex.Message})");
                }
            }
            return restored;
        }

        private static bool IsWhitelisted(string name, ISet<string> whitelist)
        {
            if (whitelist == null || whitelist.Count == 0) return false;
            string n = UserProcessLists.Normalize(name);
            if (n.Length == 0) return false;
            foreach (var w in whitelist)
            {
                if (string.Equals(UserProcessLists.Normalize(w), n, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
