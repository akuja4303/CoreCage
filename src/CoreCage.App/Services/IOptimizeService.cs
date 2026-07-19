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
}

/// <summary>Outcome of an optimize action: did it apply, and a human-readable message for the UI.</summary>
public sealed record OptimizeResult(bool Ok, string Message);
