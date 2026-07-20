using System.Linq;
using CoreCage.Core;
using CoreCage.Core.Benchmark;
using CoreCage.Core.Ledger;
using CoreCage.Core.Modes;
using CoreCage.Core.Telemetry;

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

    public int LogicalCoreCount => Environment.ProcessorCount;

    public bool ReadCoreCageEnabled() => FeatureFlags.Current.CoreCageEnabled;

    public void WriteCoreCageEnabled(bool enabled)
    {
        FeatureFlags.Current.CoreCageEnabled = enabled;
        FeatureFlags.Current.Save();
    }

    public int ReadCoreCageReservedCores() => FeatureFlags.Current.CoreCageReservedCores;

    public void WriteCoreCageReservedCores(int reservedCores)
    {
        FeatureFlags.Current.CoreCageReservedCores = reservedCores;
        FeatureFlags.Current.Save();
    }

    public IReadOnlyList<LedgerRowInfo> ReadLedgerRows() =>
        TweakLedger.Load(TweakLedger.DefaultPath()).Entries
            .Select(e => new LedgerRowInfo(e.TweakId, e.Active, e.BaselineFps, e.BaselineOnePctLow, e.AfterFps, e.AfterOnePctLow))
            .ToList();

    /// <summary>
    /// The honest A/B shape: if Gaming Mode is already active when Prove It starts, that's not a clean
    /// baseline -- revert first, bench (true tweaks-OFF), apply, bench (tweaks-ON). If it's NOT active,
    /// same shape minus the pre-revert (it's already a clean baseline): bench (off), apply, bench (on).
    /// Either way we end with tweaks ON -- there is no revert-afterward delegate, so the user is never
    /// left de-tuned by hitting Prove it. (Previously this re-applied Gaming Mode without reverting
    /// first, so both benches captured the tweaks-ON state and the reported delta was pure run-to-run
    /// variance -- see review CRITICAL-1.)
    /// </summary>
    public async Task<OptimizeResult> ProveItAsync(IProgress<string>? progress = null)
    {
        var pm = new PresentMonInterface();
        if (!pm.IsAvailable)
        {
            return new OptimizeResult(false,
                "PresentMon.exe not found (looked in " + string.Join("; ", PresentMonInterface.DefaultExeCandidates) + "). " +
                "Download it from https://github.com/GameTechDev/PresentMon/releases and place it next to CoreCage.exe (or in a 'tools' subfolder), then hit Prove it again.");
        }

        string? processName = ResolveBenchmarkProcessName();
        if (processName == null)
            return new OptimizeResult(false, "No running game found to benchmark -- launch your game, then hit Prove it.");

        string? captureError = null;
        int benchCall = 0;
        Task<FrametimeStats> Bench()
        {
            benchCall++;
            string phase = benchCall == 1
                ? $"Getting clean baseline (tweaks off) -- capturing {processName}..."
                : $"Benchmarking with tweaks on -- capturing {processName}...";
            return Task.Run(() =>
            {
                progress?.Report(phase);
                PresentMonResult result = pm.Capture(processName, seconds: 15);
                if (result.Error != null) captureError ??= result.Error;
                return result.Stats;
            });
        }

        var gaming = ModeRegistry.Get("Gaming");
        async Task Apply()
        {
            progress?.Report("Applying Gaming Mode for the on-capture...");
            if (gaming == null) return;
            ModeResult result = await gaming.ApplyAsync(progress).ConfigureAwait(false);
            if (!result.Success) throw new ProveItStepFailedException("apply", result.Summary);
        }
        async Task Revert()
        {
            progress?.Report("Reverting Gaming Mode to get a clean baseline...");
            if (gaming == null) return;
            ModeResult result = await gaming.RevertAsync(progress).ConfigureAwait(false);
            if (!result.Success) throw new ProveItStepFailedException("revert", result.Summary);
        }

        try
        {
            bool wasActive = gaming?.IsActive ?? false;
            (FrametimeStats before, FrametimeStats after) =
                await RunProveItSequenceAsync(Bench, Apply, Revert, wasActive).ConfigureAwait(false);

            if (captureError != null || before.FrameCount == 0 || after.FrameCount == 0)
                return new OptimizeResult(false, $"Benchmark capture failed: {captureError ?? "no frames captured -- is the game presenting, and is CoreCage elevated?"}");

            bool activeAfterSequence = gaming?.IsActive ?? false;
            var ledger = TweakLedger.Load(TweakLedger.DefaultPath());
            RecordWholeStackBenchmark(ledger, before, after, activeAfterSequence);
            ledger.Save();

            double fpsDelta = after.AvgFps - before.AvgFps;
            double p1Delta = after.P1LowFps - before.P1LowFps;
            return new OptimizeResult(true,
                $"Proved it: FPS {Signed(fpsDelta)}, 1% lows {Signed(p1Delta)}.");
        }
        catch (ProveItStepFailedException ex)
        {
            // CRITICAL review fix: Apply()/Revert() above used to discard ModeResult.Success and press
            // on regardless -- if re-apply failed after a successful pre-revert, bench #2 silently
            // captured the tweaks-OFF machine and the user got a fabricated "Proved it: FPS ..." success
            // message. Now any step failure throws here and aborts the sequence honestly, before any
            // ledger row is written -- no numbers are ever recorded from a corrupted A/B.
            string honestState = ex.Step == "apply"
                ? " Gaming Mode is currently OFF -- hit Gaming Mode to re-apply."
                : " Gaming Mode may still be partially active -- check the Gaming Mode status before retrying.";
            return new OptimizeResult(false, $"Prove it aborted -- {ex.Step} failed: {ex.Message}.{honestState}");
        }
        catch (Exception ex)
        {
            return new OptimizeResult(false, $"Prove it failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Thrown by the Prove-It Apply()/Revert() delegates when the underlying <see cref="ModeResult"/>
    /// reports failure -- <c>IModeModule.ApplyAsync</c>/<c>RevertAsync</c> catch internally and return
    /// <c>Success=false</c> rather than throw, so this is how that failure is turned into an honest
    /// short-circuit instead of being silently discarded (review CRITICAL finding).
    /// </summary>
    private sealed class ProveItStepFailedException : Exception
    {
        public string Step { get; }

        public ProveItStepFailedException(string step, string summary) : base(summary)
        {
            Step = step;
        }
    }

    /// <summary>
    /// Pure orchestration for the "Prove it" A/B sequence -- no PresentMon, no ModeRegistry, no OS
    /// mutation here, only the injected delegates, so this is directly unit-testable with fakes
    /// (review CRITICAL-3). <paramref name="wasActive"/> is whether Gaming Mode was already active when
    /// Prove It started: if so, <paramref name="revert"/> runs once, before the first bench, to capture
    /// a true tweaks-off baseline; if not, the sequence is bench/apply/bench unchanged. Either shape
    /// ends right after the second bench -- with tweaks applied -- and never calls revert again.
    /// </summary>
    internal static async Task<(FrametimeStats Before, FrametimeStats After)> RunProveItSequenceAsync(
        Func<Task<FrametimeStats>> bench,
        Func<Task> apply,
        Func<Task> revert,
        bool wasActive)
    {
        if (wasActive) await revert().ConfigureAwait(false);
        var runner = new AbBenchRunner(bench, apply);
        return await runner.RunAsync().ConfigureAwait(false);
    }

    /// <summary>The foreground game process, falling back to any other detected running game --
    /// there's no "the currently tuned game" concept to target otherwise. Null if none is running.</summary>
    private static string? ResolveBenchmarkProcessName()
    {
        foreach (var p in ProcessWatcher.GetRunningGameProcesses())
        {
            try { return p.ProcessName + ".exe"; }
            catch { /* exited mid-enumeration -- try the next */ }
            finally { p.Dispose(); }
        }
        return null;
    }

    /// <summary>TweakId for the single whole-stack ledger row Prove It writes to. Sourced from the
    /// shared <see cref="TweakIds.GamingStack"/> constant so the string isn't duplicated across
    /// CoreCage.App (this class and <c>OptimizeViewModel</c>) (review MINOR finding).</summary>
    internal const string WholeStackTweakId = TweakIds.GamingStack;

    /// <summary>
    /// Records the measured A/B on exactly ONE ledger row -- <see cref="WholeStackTweakId"/> -- rather
    /// than copying the identical whole-stack delta onto every active per-tweak row. A single
    /// whole-stack A/B cannot attribute causation to individual tweaks (review CRITICAL-2); the
    /// individual step rows (gaming-pipeline / eac-polish / core-cage) are left untouched here -- they
    /// keep whatever Active status GamingMode gave them, and the UI shows them as "measured as part of
    /// whole stack" instead of a copied per-tweak number. <paramref name="active"/> is the real
    /// post-sequence <c>IModeModule.IsActive</c> read, not a hardcoded assumption -- Prove It always
    /// intends to end tweaks-ON, but the row must reflect what the machine actually is, not what the
    /// sequence merely intended (review CRITICAL finding: this used to hardcode <c>true</c>).
    /// </summary>
    internal static void RecordWholeStackBenchmark(TweakLedger ledger, FrametimeStats before, FrametimeStats after, bool active)
    {
        ledger.Record(new LedgerEntry(WholeStackTweakId, DateTimeOffset.Now, active,
            before.AvgFps, before.P1LowFps, after.AvgFps, after.P1LowFps));
    }

    private static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

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
