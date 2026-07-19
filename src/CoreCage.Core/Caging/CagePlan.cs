using System.Collections.Generic;

namespace CoreCage.Core.Caging
{
    /// <summary>
    /// The output of <see cref="CoreCageService.BuildPlan"/>: which logical cores are reserved for the
    /// game, which are the cage the background processes get confined to, and which running processes
    /// (by pid) should actually be caged. Immutable — a plan is a snapshot of one decision, not a live
    /// handle; re-run <c>BuildPlan</c> if the process list changes.
    /// </summary>
    /// <param name="GameMask">Bitmask of logical cores reserved for the game (top cores).</param>
    /// <param name="CagedMask">Bitmask of logical cores background processes are confined to (bottom cores).</param>
    /// <param name="CagedPids">Pids of the non-whitelisted processes to confine to <see cref="CagedMask"/>.</param>
    public sealed record CagePlan(long GameMask, long CagedMask, IReadOnlyList<int> CagedPids);
}
