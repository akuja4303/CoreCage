using CoreCage.Core.Modes;

namespace CoreCage.App.Services;

/// <summary>
/// Real Optimize backend: drives the in-process engine's Gaming mode entirely through the
/// <c>IModeModule</c> seam (<see cref="ModeRegistry"/>.Get("Gaming")) — never calling the underlying
/// engine pipeline (GamingModePlusPlus/EacSafePriority/CoreUnpark) directly, so CoreCage.App and any
/// future private mode module stay behind the same uniform Apply/Revert contract. Every call is
/// wrapped so it never throws. CoreCage.App is gaming-only: there is no other performance-mode surface here.
/// </summary>
public sealed class EngineOptimizeService : IOptimizeService
{
    public Task<OptimizeResult> ApplyGamingAsync(IProgress<string>? progress = null) =>
        RunGamingModeAsync(m => m.ApplyAsync(progress), "Gaming Mode");

    public Task<OptimizeResult> RestoreAsync(IProgress<string>? progress = null) =>
        RunGamingModeAsync(m => m.RevertAsync(progress), "Restore");

    public bool ReadGamingIsActive() => ModeRegistry.Get("Gaming")?.IsActive ?? false;

    private static async Task<OptimizeResult> RunGamingModeAsync(Func<IModeModule, Task<ModeResult>> op, string label)
    {
        var gaming = ModeRegistry.Get("Gaming");
        if (gaming == null) return new OptimizeResult(false, "Gaming mode module not registered.");

        try
        {
            ModeResult result = await op(gaming).ConfigureAwait(false);
            return new OptimizeResult(result.Success, result.Summary);
        }
        catch (Exception ex)
        {
            return new OptimizeResult(false, $"{label} failed: {ex.Message}");
        }
    }
}
