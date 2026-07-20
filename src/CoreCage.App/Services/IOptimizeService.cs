namespace CoreCage.App.Services;

/// <summary>
/// The Optimize group's actions — the one-click Gaming mode and its Restore counterpart. Backed in
/// production by <see cref="EngineOptimizeService"/>, which drives the mode through
/// <c>CoreCage.Core.Modes.ModeRegistry.Get("Gaming")</c> (the <c>IModeModule</c> seam); tests use a
/// fake. Each call returns an <see cref="OptimizeResult"/> rather than throwing, so the UI always has
/// an honest success/failure message to show. CoreCage.App is gaming-only — there is no other
/// performance-mode surface here.
/// </summary>
public interface IOptimizeService
{
    Task<OptimizeResult> ApplyGamingAsync(IProgress<string>? progress = null);
    Task<OptimizeResult> RestoreAsync(IProgress<string>? progress = null);

    /// <summary>
    /// One-shot read of whether Gaming Mode is currently active. Backed by <c>IModeModule.IsActive</c>,
    /// which does file I/O per read — callers must read this once (e.g. on activation / after an
    /// apply-or-restore completes), never bind it directly on a polling/per-frame path.
    /// </summary>
    bool ReadGamingIsActive();

    /// <summary>How many logical cores this machine has — bounds the Core Cage reserved-cores picker.</summary>
    int LogicalCoreCount { get; }

    /// <summary>Whether Core Cage (reserve cores for the game, confine background processes to the
    /// rest) is enabled. Backed by the persisted <c>FeatureFlags.CoreCageEnabled</c>.</summary>
    bool ReadCoreCageEnabled();

    /// <summary>Persists the Core Cage on/off toggle.</summary>
    void WriteCoreCageEnabled(bool enabled);

    /// <summary>How many logical cores are reserved for the game when Core Cage applies. Backed by the
    /// persisted <c>FeatureFlags.CoreCageReservedCores</c>.</summary>
    int ReadCoreCageReservedCores();

    /// <summary>Persists the Core Cage reserved-core-count picker.</summary>
    void WriteCoreCageReservedCores(int reservedCores);

    /// <summary>
    /// Current Tweak Ledger rows -- "what's active + what it earned you". Backed by
    /// <c>CoreCage.Core.Ledger.TweakLedger</c>, which does file I/O per read (same posture as
    /// <see cref="ReadGamingIsActive"/>) -- callers read this once per refresh (after Apply/Restore/Prove
    /// it completes), never on a polling/per-frame path.
    /// </summary>
    IReadOnlyList<LedgerRowInfo> ReadLedgerRows();

    /// <summary>
    /// Runs a real PresentMon-based before/after benchmark against the currently active tweaks (via
    /// <c>CoreCage.Core.Benchmark.AbBenchRunner</c>) and, on success, updates the ledger's active rows
    /// with the measured numbers. Never throws -- an honest failure (PresentMon.exe missing, no game
    /// running, a failed capture) comes back as <c>Ok=false</c> with a message the UI can show verbatim,
    /// including a download hint when PresentMon itself is the problem (it is not committed to the
    /// repo -- the user/CI fetches it separately).
    /// </summary>
    Task<OptimizeResult> ProveItAsync(IProgress<string>? progress = null);
}

/// <summary>Outcome of an optimize action: did it apply, and a human-readable message for the UI.</summary>
public sealed record OptimizeResult(bool Ok, string Message);

/// <summary>
/// One Tweak Ledger row for display: which tweak, whether it's currently active, and its measured
/// before/after numbers (all null until a "Prove it" run fills them in -- the UI shows "not yet
/// benchmarked" for that case). Mirrors <c>CoreCage.Core.Ledger.LedgerEntry</c> field-for-field so
/// <see cref="EngineOptimizeService"/> can map it 1:1; kept as its own App-layer type so
/// <see cref="OptimizeViewModel"/> never has to reference CoreCage.Core.Ledger directly.
/// </summary>
public sealed record LedgerRowInfo(
    string TweakId,
    bool Active,
    double? BaselineFps,
    double? BaselineOnePctLow,
    double? AfterFps,
    double? AfterOnePctLow);
