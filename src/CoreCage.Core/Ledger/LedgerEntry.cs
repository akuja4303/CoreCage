using System;

namespace CoreCage.Core.Ledger
{
    /// <summary>
    /// One row of the Tweak Ledger — "what's active + what it earned you" (the FPS-chasing gamer wants
    /// proof, not promises). <see cref="TweakId"/> identifies the pipeline step (e.g. "gaming-pipeline",
    /// "eac-polish", "core-unpark", "core-cage"); <see cref="Active"/> tracks whether it's currently
    /// applied. The four benchmark fields are null until a "Prove it" A/B run (<see
    /// cref="CoreCage.Core.Benchmark.AbBenchRunner"/>) fills them in — the UI shows "not yet
    /// benchmarked" for a row with nulls, and the measured delta once they're populated.
    /// </summary>
    public sealed record LedgerEntry(
        string TweakId,
        DateTimeOffset AppliedAt,
        bool Active,
        double? BaselineFps,
        double? BaselineOnePctLow,
        double? AfterFps,
        double? AfterOnePctLow);
}
