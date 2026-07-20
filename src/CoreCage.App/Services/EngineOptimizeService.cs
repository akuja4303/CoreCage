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
        Task<FrametimeStats> Bench()
        {
            return Task.Run(() =>
            {
                progress?.Report($"Capturing {processName}...");
                PresentMonResult result = pm.Capture(processName, seconds: 15);
                if (result.Error != null) captureError ??= result.Error;
                return result.Stats;
            });
        }

        var gaming = ModeRegistry.Get("Gaming");
        async Task Apply()
        {
            progress?.Report("Re-applying Gaming Mode for the after-capture...");
            if (gaming != null) await gaming.ApplyAsync(progress).ConfigureAwait(false);
        }

        try
        {
            var runner = new AbBenchRunner(Bench, Apply);
            (FrametimeStats before, FrametimeStats after) = await runner.RunAsync().ConfigureAwait(false);

            if (captureError != null || before.FrameCount == 0 || after.FrameCount == 0)
                return new OptimizeResult(false, $"Benchmark capture failed: {captureError ?? "no frames captured -- is the game presenting, and is CoreCage elevated?"}");

            RecordBenchmarkOnActiveTweaks(before, after);

            double fpsDelta = after.AvgFps - before.AvgFps;
            double p1Delta = after.P1LowFps - before.P1LowFps;
            return new OptimizeResult(true,
                $"Proved it: FPS {Signed(fpsDelta)}, 1% lows {Signed(p1Delta)}.");
        }
        catch (Exception ex)
        {
            return new OptimizeResult(false, $"Prove it failed: {ex.Message}");
        }
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

    private static void RecordBenchmarkOnActiveTweaks(FrametimeStats before, FrametimeStats after)
    {
        var ledger = TweakLedger.Load(TweakLedger.DefaultPath());
        foreach (var entry in ledger.Entries.Where(e => e.Active).ToList())
        {
            ledger.Record(entry with
            {
                BaselineFps = before.AvgFps,
                BaselineOnePctLow = before.P1LowFps,
                AfterFps = after.AvgFps,
                AfterOnePctLow = after.P1LowFps,
            });
        }
        ledger.Save();
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
