using System.Threading;
using CoreCage.App.Services;

namespace CoreCage.Tests;

/// <summary>
/// In-memory <see cref="IOptimizeService"/> so the Optimize VM is tested WITHOUT ever running the
/// real engine (which would tweak the test machine). Records calls, lets a test force a failure, and
/// can hold an action open (Entered/Release gates) to prove the busy-state disables the buttons.
/// </summary>
internal sealed class FakeOptimizeService : IOptimizeService
{
    public int GamingCalls, RestoreCalls;
    public bool ThrowOnGaming;
    public bool GamingActive;

    public OptimizeResult GamingResult { get; set; } = new(true, "Gaming Mode applied.");
    public OptimizeResult RestoreResult { get; set; } = new(true, "Restored 12 changes.");

    /// <summary>Signals that ApplyGamingAsync has started.</summary>
    public readonly ManualResetEventSlim Entered = new(false);
    /// <summary>Open by default; a test can Reset() it to hold ApplyGamingAsync mid-flight.</summary>
    public readonly ManualResetEventSlim Release = new(true);

    public Task<OptimizeResult> ApplyGamingAsync(IProgress<string>? progress = null)
    {
        GamingCalls++;
        Entered.Set();
        Release.Wait();
        if (ThrowOnGaming) throw new InvalidOperationException("boom");
        GamingActive = true;
        return Task.FromResult(GamingResult);
    }

    public Task<OptimizeResult> RestoreAsync(IProgress<string>? progress = null)
    {
        RestoreCalls++;
        GamingActive = false;
        return Task.FromResult(RestoreResult);
    }

    public bool ReadGamingIsActive() => GamingActive;
}
