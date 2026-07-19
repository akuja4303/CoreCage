using System;
using System.Collections.Generic;

namespace CoreCage.Core.Telemetry
{
    /// <summary>
    /// Pure, dependency-free frametime statistics — the honest "before/after" numbers a tuner must
    /// be able to prove (Council 2026-06-01 rank 2). Built from a list of per-frame frametimes in
    /// milliseconds (PresentMon's <c>MsBetweenPresents</c> column). All math is deterministic and
    /// unit-tested; no IO, no process launch, no UI — see <see cref="PresentMonInterface"/> for the
    /// capture side and <see cref="PresentMonCsv"/> for parsing.
    ///
    /// Definitions follow the common CapFrameX/percentile convention:
    ///   • Avg FPS      = 1000 / mean(frametime)
    ///   • 1% low FPS   = 1000 / (99th-percentile frametime)   — i.e. the FPS during the worst 1% of frames
    ///   • 0.1% low FPS = 1000 / (99.9th-percentile frametime)
    /// A higher frametime is a worse frame, so the high-percentile frametime maps to the low-FPS figure.
    /// </summary>
    public sealed class FrametimeStats
    {
        /// <summary>Number of frames the statistics were computed from.</summary>
        public int FrameCount { get; }
        /// <summary>Arithmetic mean frametime (ms).</summary>
        public double AvgFrameTimeMs { get; }
        /// <summary>Average frames per second (1000 / mean frametime).</summary>
        public double AvgFps { get; }
        /// <summary>1% low FPS = 1000 / 99th-percentile frametime.</summary>
        public double P1LowFps { get; }
        /// <summary>0.1% low FPS = 1000 / 99.9th-percentile frametime.</summary>
        public double P01LowFps { get; }
        /// <summary>99th-percentile frametime (ms) — the "worst 1%" frame time.</summary>
        public double P99FrameTimeMs { get; }
        /// <summary>Population standard deviation of frametimes (ms) — a frame-pacing/stutter proxy.</summary>
        public double StdDevMs { get; }
        /// <summary>Best single-frame FPS (1000 / min frametime).</summary>
        public double MaxFps { get; }
        /// <summary>Worst single-frame FPS (1000 / max frametime).</summary>
        public double MinFps { get; }

        private FrametimeStats(int frameCount, double avgMs, double avgFps, double p1, double p01,
                               double p99Ms, double stdDevMs, double maxFps, double minFps)
        {
            FrameCount = frameCount;
            AvgFrameTimeMs = avgMs;
            AvgFps = avgFps;
            P1LowFps = p1;
            P01LowFps = p01;
            P99FrameTimeMs = p99Ms;
            StdDevMs = stdDevMs;
            MaxFps = maxFps;
            MinFps = minFps;
        }

        /// <summary>An all-zero result for an empty capture (no frames).</summary>
        public static FrametimeStats Empty { get; } =
            new FrametimeStats(0, 0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>
        /// Computes statistics from per-frame frametimes (ms). Non-positive, NaN and infinite samples
        /// are ignored (PresentMon emits "NA" / 0 rows that are not real frames). Returns
        /// <see cref="Empty"/> when no valid frames remain.
        /// </summary>
        public static FrametimeStats FromFrametimes(IReadOnlyList<double> frameTimesMs)
        {
            if (frameTimesMs == null) throw new ArgumentNullException(nameof(frameTimesMs));

            var valid = new List<double>(frameTimesMs.Count);
            foreach (var ft in frameTimesMs)
            {
                if (ft > 0 && !double.IsNaN(ft) && !double.IsInfinity(ft))
                    valid.Add(ft);
            }
            if (valid.Count == 0) return Empty;

            valid.Sort(); // ascending: small (good) → large (bad) frametimes

            double sum = 0;
            foreach (var v in valid) sum += v;
            double mean = sum / valid.Count;

            double sqDiff = 0;
            foreach (var v in valid) { double d = v - mean; sqDiff += d * d; }
            double stdDev = Math.Sqrt(sqDiff / valid.Count);

            double p99Ms = Percentile(valid, 99.0);
            double p999Ms = Percentile(valid, 99.9);
            double minMs = valid[0];
            double maxMs = valid[valid.Count - 1];

            return new FrametimeStats(
                frameCount: valid.Count,
                avgMs: mean,
                avgFps: 1000.0 / mean,
                p1: 1000.0 / p99Ms,
                p01: 1000.0 / p999Ms,
                p99Ms: p99Ms,
                stdDevMs: stdDev,
                maxFps: 1000.0 / minMs,
                minFps: 1000.0 / maxMs);
        }

        /// <summary>
        /// Linear-interpolation percentile (type R-7, the Excel PERCENTILE.INC / numpy-default method)
        /// over an <b>already-ascending-sorted</b> list. <paramref name="p"/> is in [0,100].
        /// </summary>
        public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
        {
            if (sortedAscending == null) throw new ArgumentNullException(nameof(sortedAscending));
            int n = sortedAscending.Count;
            if (n == 0) return 0;
            if (n == 1) return sortedAscending[0];
            if (p <= 0) return sortedAscending[0];
            if (p >= 100) return sortedAscending[n - 1];

            double rank = (p / 100.0) * (n - 1); // 0-based fractional index
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sortedAscending[lo];
            double frac = rank - lo;
            return sortedAscending[lo] + frac * (sortedAscending[hi] - sortedAscending[lo]);
        }
    }
}
