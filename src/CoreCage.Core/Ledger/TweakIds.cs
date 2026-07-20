namespace CoreCage.Core.Ledger
{
    /// <summary>
    /// Well-known <see cref="LedgerEntry.TweakId"/> values shared across the ledger, the Prove-It
    /// benchmark recorder, and the App-layer display mapping. Single source of truth for the
    /// "gaming-stack" string, which was previously duplicated as separate literals in
    /// <c>EngineOptimizeService</c> and <c>OptimizeViewModel</c> (review MINOR finding).
    /// </summary>
    public static class TweakIds
    {
        /// <summary>TweakId for the single whole-stack row Prove It records its A/B benchmark to.</summary>
        public const string GamingStack = "gaming-stack";
    }
}
