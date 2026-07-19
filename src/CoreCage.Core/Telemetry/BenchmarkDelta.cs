using System;
using System.Globalization;

namespace CoreCage.Core.Telemetry
{
    /// <summary>
    /// Pure before/after comparison of two <see cref="FrametimeStats"/> captures — the proof a user
    /// sees after toggling Gaming Mode (Council 2026-06-01 rank 2). Turns a baseline + an after-tune
    /// capture into signed deltas and percentages for avg FPS, 1% low, 0.1% low and frametime.
    /// No IO/UI — formats a one-line summary the UI/log can show verbatim.
    /// </summary>
    public sealed class BenchmarkDelta
    {
        public FrametimeStats Before { get; }
        public FrametimeStats After { get; }

        public double AvgFpsDelta { get; }
        public double AvgFpsPercent { get; }
        public double P1LowFpsDelta { get; }
        public double P1LowFpsPercent { get; }
        public double P01LowFpsDelta { get; }
        public double P01LowFpsPercent { get; }
        /// <summary>Frametime delta (ms); negative = improvement (lower frametime is better).</summary>
        public double AvgFrameTimeMsDelta { get; }

        private BenchmarkDelta(FrametimeStats before, FrametimeStats after)
        {
            Before = before;
            After = after;

            AvgFpsDelta = after.AvgFps - before.AvgFps;
            AvgFpsPercent = Percent(before.AvgFps, after.AvgFps);
            P1LowFpsDelta = after.P1LowFps - before.P1LowFps;
            P1LowFpsPercent = Percent(before.P1LowFps, after.P1LowFps);
            P01LowFpsDelta = after.P01LowFps - before.P01LowFps;
            P01LowFpsPercent = Percent(before.P01LowFps, after.P01LowFps);
            AvgFrameTimeMsDelta = after.AvgFrameTimeMs - before.AvgFrameTimeMs;
        }

        public static BenchmarkDelta Between(FrametimeStats before, FrametimeStats after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            return new BenchmarkDelta(before, after);
        }

        /// <summary>Percent change from <paramref name="from"/> to <paramref name="to"/>; 0 when the base is 0.</summary>
        private static double Percent(double from, double to)
        {
            if (from == 0 || !double.IsFinite(from) || !double.IsFinite(to)) return 0;
            return (to - from) / from * 100.0;
        }

        /// <summary>e.g. "Avg 142.0 → 151.8 fps (+6.9%) · 1% low 96.1 → 101.4 (+5.5%)".</summary>
        public string Summary()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Format(c,
                "Avg {0:0.0} → {1:0.0} fps ({2:+0.0;-0.0;0}%) · 1% low {3:0.0} → {4:0.0} ({5:+0.0;-0.0;0}%) · 0.1% low {6:0.0} → {7:0.0} ({8:+0.0;-0.0;0}%)",
                Before.AvgFps, After.AvgFps, AvgFpsPercent,
                Before.P1LowFps, After.P1LowFps, P1LowFpsPercent,
                Before.P01LowFps, After.P01LowFps, P01LowFpsPercent);
        }
    }
}
