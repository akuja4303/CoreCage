using System;
using System.Collections.Generic;
using CoreCage.Core.Telemetry;

namespace CoreCage.Core.Tuning
{
    /// <summary>One tested GPU core-clock offset and how it did.</summary>
    public sealed class OffsetTrial
    {
        public int Offset { get; }
        /// <summary>True if the offset held without a driver TDR / artifact / apply failure.</summary>
        public bool Stable { get; }
        /// <summary>Measured fitness at this offset (e.g. avg FPS from PresentMon); 0 when unstable.</summary>
        public double FitnessFps { get; }

        public OffsetTrial(int offset, bool stable, double fitnessFps)
        {
            Offset = offset;
            Stable = stable;
            FitnessFps = fitnessFps;
        }
    }

    /// <summary>Bounds + step for the GPU offset search. CORE clock only — memory OC is never touched.</summary>
    public sealed class GpuAutoTuneConfig
    {
        public int MinOffset { get; init; } = 0;
        public int MaxOffset { get; init; } = 300;     // hard ceiling for the core offset search
        public int Step { get; init; } = 30;           // climb increment (MHz)
        public double NoiseFps { get; init; } = 1.0;    // fitness gains within this are treated as noise

        public static GpuAutoTuneConfig Default => new();
    }

    /// <summary>
    /// Pure decision logic for the GPU core-offset auto-tune (Council rank 13 — the data-driven answer
    /// to "how far can I push the offset?"). Stability-gated climb: step the offset up while each step
    /// yields a real (beyond-noise) FPS gain and stays stable; stop at the first instability or plateau.
    /// The recommendation deliberately picks the LOWEST offset that reaches near-best measured FPS — same
    /// performance at the most conservative (most stable) clock, which is the built-in safety margin
    /// (mirrors the CPU-CO runbook's "settle at the 2nd-to-last passing value", not at the cliff).
    /// No IO/hardware — drive it with <see cref="GpuAutoTuner"/>.
    /// </summary>
    public static class GpuAutoTunePolicy
    {
        /// <summary>The next offset to test, or <c>null</c> when the search has converged.</summary>
        public static int? NextOffset(IReadOnlyList<OffsetTrial> trials, GpuAutoTuneConfig cfg)
        {
            if (trials == null) throw new ArgumentNullException(nameof(trials));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            if (trials.Count == 0) return cfg.MinOffset;     // always baseline at MinOffset first

            var last = trials[trials.Count - 1];
            if (!last.Stable) return null;                   // hit the instability ceiling — stop climbing

            // Plateau: if the most recent (higher) offset didn't beat the best earlier fitness by more
            // than the noise floor, pushing higher buys nothing — stop.
            if (trials.Count >= 2)
            {
                double bestBefore = double.NegativeInfinity;
                for (int i = 0; i < trials.Count - 1; i++)
                    if (trials[i].FitnessFps > bestBefore) bestBefore = trials[i].FitnessFps;
                if (last.FitnessFps <= bestBefore + cfg.NoiseFps) return null;
            }

            int next = last.Offset + cfg.Step;
            if (next > cfg.MaxOffset) return null;
            return next;
        }

        /// <summary>
        /// The recommended offset given everything tested: the lowest stable offset whose measured FPS is
        /// within the noise floor of the best result. Falls back to <see cref="GpuAutoTuneConfig.MinOffset"/>
        /// (the safe baseline) when nothing stable was found.
        /// </summary>
        public static int Recommend(IReadOnlyList<OffsetTrial> trials, GpuAutoTuneConfig cfg)
        {
            if (trials == null) throw new ArgumentNullException(nameof(trials));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double best = double.NegativeInfinity;
            bool anyStable = false;
            foreach (var t in trials)
                if (t.Stable) { anyStable = true; if (t.FitnessFps > best) best = t.FitnessFps; }

            if (!anyStable) return cfg.MinOffset;

            int rec = cfg.MinOffset;
            int recOffset = int.MaxValue;
            foreach (var t in trials)
            {
                if (!t.Stable) continue;
                if (t.FitnessFps >= best - cfg.NoiseFps && t.Offset < recOffset)
                {
                    recOffset = t.Offset;
                    rec = t.Offset;
                }
            }
            return rec;
        }
    }

    /// <summary>Outcome of an auto-tune run.</summary>
    public sealed class GpuAutoTuneResult
    {
        public int RecommendedOffsetMhz { get; }
        public IReadOnlyList<OffsetTrial> Trials { get; }
        public bool HitInstability { get; }

        public GpuAutoTuneResult(int recommended, IReadOnlyList<OffsetTrial> trials, bool hitInstability)
        {
            RecommendedOffsetMhz = recommended;
            Trials = trials;
            HitInstability = hitInstability;
        }
    }

    /// <summary>
    /// Orchestrates the GPU offset auto-tune by driving <see cref="GpuAutoTunePolicy"/> against injected
    /// seams, so the whole loop is unit-testable with fakes and the live wiring stays thin. Live use
    /// (Council rank 13) supplies: applyOffset = <c>IGpuController.SetCoreClockOffsetMhz</c>, measure =
    /// <c>PresentMonInterface.Capture(...).Stats</c>, isStable = a TDR/artifact check.
    ///
    /// ⚠️ SUPERVISED + gated on rank 6 (a non-stale NvAPI build + a real TDR watchdog) before live use —
    /// an unvalidated offset can TDR the driver mid-game. The search itself never exceeds the first
    /// instability it observes, and always leaves the GPU at the recommended (safe) offset.
    /// </summary>
    public sealed class GpuAutoTuner
    {
        private readonly Func<int, bool> _applyOffset;     // set offset; false = apply failed (clamp/TDR/unsupported)
        private readonly Func<bool> _isStable;             // post-apply stability check; false = TDR/artifact
        private readonly Func<FrametimeStats> _measure;    // capture fitness at the current offset
        private readonly Func<FrametimeStats, double> _fitness;

        public GpuAutoTuner(
            Func<int, bool> applyOffset,
            Func<bool> isStable,
            Func<FrametimeStats> measure,
            Func<FrametimeStats, double>? fitness = null)
        {
            _applyOffset = applyOffset ?? throw new ArgumentNullException(nameof(applyOffset));
            _isStable = isStable ?? throw new ArgumentNullException(nameof(isStable));
            _measure = measure ?? throw new ArgumentNullException(nameof(measure));
            _fitness = fitness ?? (s => s.AvgFps);   // default fitness = average FPS
        }

        /// <summary>Convenience: drive applyOffset from a live <see cref="IGpuController"/>.</summary>
        public static GpuAutoTuner FromController(
            IGpuController gpu, Func<bool> isStable, Func<FrametimeStats> measure,
            Func<FrametimeStats, double>? fitness = null)
            => new GpuAutoTuner(gpu.SetCoreClockOffsetMhz, isStable, measure, fitness);

        /// <summary>Runs the stability-gated climb and leaves the GPU at the recommended safe offset.</summary>
        public GpuAutoTuneResult Run(GpuAutoTuneConfig? config = null)
        {
            var cfg = config ?? GpuAutoTuneConfig.Default;
            var trials = new List<OffsetTrial>();
            bool hitInstability = false;

            int? candidate = GpuAutoTunePolicy.NextOffset(trials, cfg);
            while (candidate.HasValue)
            {
                int off = candidate.Value;
                bool applied = _applyOffset(off);

                // Measure FIRST — this is the workload window during which an unstable offset would TDR.
                FrametimeStats stats = FrametimeStats.Empty;
                if (applied)
                {
                    try { stats = _measure(); } catch { applied = false; }
                }

                // Judge stability AFTER the workload so a TDR during measurement is attributed to this offset.
                bool stable = applied && _isStable();
                double fit = stable ? _fitness(stats) : 0;
                if (!stable) hitInstability = true;

                trials.Add(new OffsetTrial(off, stable, fit));
                candidate = GpuAutoTunePolicy.NextOffset(trials, cfg);
            }

            int recommended = GpuAutoTunePolicy.Recommend(trials, cfg);
            _applyOffset(recommended);   // never leave the card at the last (possibly unstable) probe
            return new GpuAutoTuneResult(recommended, trials, hitInstability);
        }
    }
}
